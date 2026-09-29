namespace GitAutoBackup.Services;

/// <summary>
/// 翻译后的友好错误：把 git / gh / GitHub API 的原始英文报错，
/// 转换成分层、通俗、可执行的说明。
/// </summary>
/// <param name="Title">一句话说明发生了什么（用于弹窗标题、状态栏）。</param>
/// <param name="Explanation">为什么会这样。</param>
/// <param name="Suggestion">用户该怎么做。</param>
/// <param name="Retryable">重试是否有意义。false 表示重试必然再次失败，不该再问用户。</param>
/// <param name="Raw">原始错误全文（供详细查看 / 反馈给开发者）。</param>
/// <param name="Category">分类标识，便于调用方做特殊处理。</param>
public sealed record FriendlyError(
    string Title,
    string Explanation,
    string Suggestion,
    bool Retryable,
    string Raw,
    string Category)
{
    /// <summary>适合日志单行输出的简短描述。</summary>
    public string Short => $"{Title}（{Category}）";
}

/// <summary>
/// 把底层技术错误翻译成用户能理解的语言。
///
/// 背景：直接把 git 的原始输出抛给用户是没有意义的 ——
/// 例如「remote: error: GH001: Large files detected」对普通用户完全不可读。
/// 这里按关键词识别常见错误，给出「发生了什么 + 为什么 + 怎么办」。
/// </summary>
public static class ErrorTranslator
{
    /// <summary>
    /// 构造「推送超时」的友好错误。
    ///
    /// 超时不是 git 返回的错误码，而是 <see cref="ProcessRunner"/> 等待超时后 Kill 进程抛出的异常，
    /// 因此没有可解析的输出，必须单独构造 —— 否则会被归入"未知错误"，用户看不到"网络慢"这个关键判断。
    /// </summary>
    public static FriendlyError Timeout(int seconds) => new(
        "推送超时",
        $"连接 GitHub 已等待 {seconds} 秒仍未完成。常见原因是网络较慢、需要代理，或本次备份的内容较大。",
        "可以继续重试（重试会自动延长等待时间）；若反复超时，请检查网络连接，或为 git 配置代理后再试。",
        true,
        $"命令超时：{seconds} 秒内未完成，进程已被终止。",
        "推送超时");

    /// <summary>把原始错误翻译成友好说明。无法识别时返回"未知错误"（可重试）。</summary>
    public static FriendlyError Translate(string? raw)    {
        var e = raw ?? string.Empty;
        if (e.Length == 0)
        {
            return new FriendlyError(
                "推送失败", "GitHub 没有返回具体的错误信息。", "可以重试；若反复失败请查看日志。",
                true, e, "未知");
        }

        // ---------- 1. 单文件超过 GitHub 上限（最常见，且重试必然失败） ----------
        if (Contains(e, "GH001")
            || Contains(e, "Large files detected")
            || Contains(e, "file is too large")
            || Contains(e, "exceeds GitHub's file size limit")
            || Contains(e, "this exceeds GitHub"))
        {
            return new FriendlyError(
                "有文件超过 GitHub 的大小限制",
                "GitHub 不接受大于 100 MB 的单个文件，因此这次推送被直接拒绝了。",
                "把该文件加入「排除规则」后重新备份；若它已经在更早的提交里，需要清理仓库历史（软件无法自动完成这一步）。",
                false, e, "文件过大");
        }

        // ---------- 2. 单次推送总量超过 2 GB ----------
        if (Contains(e, "pack exceeds maximum allowed size")
            || Contains(e, "exceeds the maximum allowed size"))
        {
            return new FriendlyError(
                "单次推送的内容太大",
                "GitHub 限制单次推送不能超过 2 GB，本次推送超限。",
                "排除掉大文件，或把项目拆成多次备份。",
                false, e, "推送过大");
        }

        // ---------- 3. 认证失败 / 无权限（重试无用，必须重新登录） ----------
        if (Contains(e, "Authentication failed")
            || Contains(e, "could not read Username")
            || Contains(e, "Invalid username or password")
            || Contains(e, "Support for password authentication was removed")
            || Contains(e, "Bad credentials")
            || Contains(e, "Permission to")
            || Contains(e, "Permission denied")
            || Contains(e, "not authorized")
            || Contains(e, "403"))
        {
            return new FriendlyError(
                "GitHub 登录已失效或权限不足",
                "GitHub 拒绝了这次操作。可能是登录凭据过期，或当前账号没有这个仓库的写入权限。",
                "点左下角账号入口重新登录 GitHub；若使用 gh 命令行登录，请先执行 gh auth login 再重试。",
                false, e, "认证失败");
        }

        // ---------- 4. 仓库不存在 ----------
        if (Contains(e, "Repository not found")
            || Contains(e, "could not be found")
            || Contains(e, "404"))
        {
            return new FriendlyError(
                "找不到目标仓库",
                "GitHub 上没有找到这个仓库，可能已被删除，或当前账号无权访问。",
                "确认 GitHub 账号与仓库名是否正确；若仓库已被删除，重新执行一次备份会自动创建。",
                false, e, "仓库不存在");
        }

        // ---------- 5. 磁盘空间不足 ----------
        if (Contains(e, "No space left on device")
            || Contains(e, "insufficient permission for adding an object"))
        {
            return new FriendlyError(
                "本地磁盘空间不足",
                "写入备份文件时磁盘空间不够了。",
                "清理磁盘空间，或把「备份根目录」改到空间充足的分区后再备份。",
                false, e, "磁盘空间");
        }

        // ---------- 6. 缺少删除仓库权限 ----------
        if (Contains(e, "delete_repo"))
        {
            return new FriendlyError(
                "没有删除仓库的权限",
                "删除 GitHub 仓库需要额外的 delete_repo 权限，当前登录尚未开通。",
                "点左下角账号入口 →「解锁删除权限」，完成授权后重试。",
                false, e, "缺少权限");
        }

        // ---------- 7. 网络问题（可重试） ----------
        if (Contains(e, "Could not resolve host")
            || Contains(e, "unable to access")
            || Contains(e, "Failed to connect")
            || Contains(e, "Couldn't connect to server")
            || Contains(e, "Connection timed out")
            || Contains(e, "Connection reset")
            || Contains(e, "Operation timed out")
            || Contains(e, "timed out")
            || Contains(e, "timeout")
            || Contains(e, "Recv failure")
            || Contains(e, "early EOF")
            || Contains(e, "SSL certificate problem")
            || Contains(e, "TLS")
            || Contains(e, "network"))
        {
            return new FriendlyError(
                "网络无法连接 GitHub",
                "连接 github.com 时中断了，可能是网络不稳定、需要代理，或 GitHub 暂时不可用。",
                "检查网络后重试；如果使用代理，请确认 git 已配置代理（可点日志面板旁的终端执行 git config --global http.proxy）。",
                true, e, "网络问题");
        }

        // ---------- 8. 远端有本地没有的提交（分叉，会自动合并后重试） ----------
        if (Contains(e, "rejected")
            || Contains(e, "non-fast-forward")
            || Contains(e, "fetch first")
            || Contains(e, "failed to push some refs"))
        {
            return new FriendlyError(
                "云端仓库有更新的内容",
                "远程仓库存在本地没有的提交，直接推送被拒绝。",
                "软件会自动尝试合并后重试；若仍失败，可在内置终端手动执行 git pull --rebase。",
                true, e, "历史分叉");
        }

        // ---------- 9. 分支保护 ----------
        if (Contains(e, "protected branch"))
        {
            return new FriendlyError(
                "目标分支受保护",
                "GitHub 上该分支开启了保护规则，不允许直接推送。",
                "在 GitHub 仓库设置里调整分支保护规则，或改用其他分支备份。",
                false, e, "分支保护");
        }

        // ---------- 未知 ----------
        return new FriendlyError(
            "推送失败",
            "GitHub 返回了一个未预期的错误，软件无法自动识别原因。",
            "可以先重试一次；若反复失败，请把下面的原始信息发给开发者以便排查。",
            true, e, "未知");
    }

    private static bool Contains(string text, string keyword)
        => text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
