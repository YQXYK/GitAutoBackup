"""
从一张源图生成 Windows 应用图标（多尺寸 .ico + 512 大图）。

用法：
    python make_icon.py <源图路径> [输出目录]

设计要点（重要，别改坏）：
1. **裁掉四周空白** —— 源图通常是圆角方块 + 大片留白，直接缩到 16px 会让图标
   只占中间一小块。先找内容边界再裁，让图标在小尺寸下尽量占满。
2. **小尺寸用简化版** —— 细节丰富（代码行 / 云 / 箭头）的图在 16~24px 会糊成一团。
   所以 < 32px 用程序绘制的极简剪影（文件夹 + 云 + 青色箭头），>= 32px 用原图缩放。
   这是让任务栏 / 资源管理器里也清晰的关键。
3. **LANCZOS 重采样** —— 缩小时质量最好；简化版先按 8 倍大尺寸绘制再缩，
   得到平滑边缘（否则会有锯齿）。
4. **导出预览图** —— 并排放大各尺寸（NEAREST 保留像素），肉眼检查清晰度。
"""
import os
import sys
from PIL import Image, ImageDraw

# Windows 图标标准尺寸（256 必须存在，资源管理器大图标要用）
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# 简化版配色（取自源图）
NAVY = (26, 48, 84, 255)
CYAN = (34, 196, 222, 255)
LIGHT = (232, 244, 252, 255)

# 低于此尺寸改用简化版
SIMPLE_THRESHOLD = 32


def content_bbox(img, tolerance=12, step=2):
    """找出非白色/非透明的实际内容区域（带 2px 内边距）。"""
    rgb = img.convert("RGB")
    w, h = rgb.size
    px = rgb.load()
    min_x, min_y, max_x, max_y = w, h, -1, -1
    for y in range(0, h, step):
        for x in range(0, w, step):
            r, g, b = px[x, y]
            if r < 255 - tolerance or g < 255 - tolerance or b < 255 - tolerance:
                min_x = min(min_x, x); min_y = min(min_y, y)
                max_x = max(max_x, x); max_y = max(max_y, y)
    if max_x < 0:
        return (0, 0, w, h)
    return (max(0, min_x - 2), max(0, min_y - 2),
            min(w, max_x + 3), min(h, max_y + 3))


def to_square(img):
    """裁剪内容后补成正方形，避免非等比缩放变形。"""
    trimmed = img.crop(content_bbox(img))
    side = max(trimmed.width, trimmed.height)
    out = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    out.paste(trimmed, ((side - trimmed.width) // 2, (side - trimmed.height) // 2), trimmed)
    return out


def make_simple(size):
    """画极简版：圆角方底 + 文件夹 + 云 + 青色箭头。小尺寸下只保留可辨识的剪影。"""
    S = size * 8  # 先大后小，得到平滑边缘
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    m = S * 0.06
    r = S * 0.20
    stroke = max(2, int(S * 0.055))

    # 圆角方底
    d.rounded_rectangle([m, m, S - m, S - m], radius=r, fill=LIGHT,
                        outline=NAVY, width=stroke)

    # 文件夹
    fx0, fy0 = S * 0.22, S * 0.34
    fx1, fy1 = S * 0.66, S * 0.70
    tab_h = S * 0.07
    d.rounded_rectangle([fx0, fy0 + tab_h, fx1, fy1], radius=S * 0.04,
                        fill=(255, 255, 255, 255), outline=NAVY, width=stroke)
    d.rounded_rectangle([fx0, fy0, fx0 + (fx1 - fx0) * 0.45, fy0 + tab_h * 2.2],
                        radius=S * 0.03, fill=(255, 255, 255, 255),
                        outline=NAVY, width=stroke)

    # 云（三个圆 + 底部长方）
    cx, cy = S * 0.66, S * 0.60
    cr = S * 0.13
    for dx, dy, rr in [(-cr * 0.75, cr * 0.15, cr * 0.72),
                       (0, -cr * 0.30, cr * 0.95),
                       (cr * 0.80, cr * 0.10, cr * 0.75)]:
        d.ellipse([cx + dx - rr, cy + dy - rr, cx + dx + rr, cy + dy + rr],
                  fill=(255, 255, 255, 255), outline=NAVY, width=stroke)
    d.rounded_rectangle([cx - cr * 1.45, cy + cr * 0.20, cx + cr * 1.50, cy + cr * 0.95],
                        radius=cr * 0.35, fill=(255, 255, 255, 255),
                        outline=NAVY, width=stroke)

    # 青色上传箭头（小尺寸下唯一的色彩锚点）
    aw = S * 0.075
    ax, ay0, ay1 = cx, cy - cr * 0.55, cy + cr * 0.85
    d.rounded_rectangle([ax - aw / 2, ay0 + aw, ax + aw / 2, ay1],
                        radius=aw * 0.2, fill=CYAN)
    d.polygon([(ax - aw * 1.15, ay0 + aw * 1.15),
               (ax + aw * 1.15, ay0 + aw * 1.15),
               (ax, ay0 - aw * 0.25)], fill=CYAN)

    return img.resize((size, size), Image.LANCZOS)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        print("错误：请提供源图路径")
        sys.exit(1)

    src_path = sys.argv[1]
    out_dir = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.abspath(src_path))

    if not os.path.isfile(src_path):
        print(f"错误：找不到源图 {src_path}")
        sys.exit(1)

    os.makedirs(out_dir, exist_ok=True)

    src = Image.open(src_path).convert("RGBA")
    print(f"源图: {src_path}")
    print(f"尺寸: {src.width} x {src.height}")

    square = to_square(src)
    print(f"裁剪后: {square.width} x {square.height}")

    frames = {}
    for s in SIZES:
        if s < SIMPLE_THRESHOLD:
            frames[s] = make_simple(s)
            kind = "简化版"
        else:
            frames[s] = square.resize((s, s), Image.LANCZOS)
            kind = "原图"
        print(f"  {s:>3}px -> {kind}")

    # 写多尺寸 ico
    ico_path = os.path.join(out_dir, "app.ico")
    frames[256].save(ico_path, format="ICO", sizes=[(s, s) for s in SIZES])
    print(f"\n[OK] {ico_path}  ({os.path.getsize(ico_path):,} bytes)")

    # 512 大图（README / Release 配图）
    png_path = os.path.join(out_dir, "app-512.png")
    square.resize((512, 512), Image.LANCZOS).save(png_path, format="PNG")
    print(f"[OK] {png_path}  ({os.path.getsize(png_path):,} bytes)")

    # 预览图：各尺寸放大并排，便于肉眼检查
    gap = 12
    preview = Image.new("RGBA", (256 * len(SIZES) + gap * (len(SIZES) + 1), 300),
                        (245, 247, 250, 255))
    x = gap
    for s in SIZES:
        big = frames[s].resize((256, 256), Image.NEAREST)
        preview.paste(big, (x, 22), big)
        x += 256 + gap
    prev_path = os.path.join(out_dir, "_preview.png")
    preview.save(prev_path)
    print(f"[OK] {prev_path}  (检查用，可删除)")


if __name__ == "__main__":
    main()
