using Dominatus.Assets.Toml;
using Dominatus.GodotConn.Assets;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Dominatus.SpriteAtlasPreview -- <atlas.sprite.toml> [--out <preview.png>]");
    return 1;
}

var tomlPath = Path.GetFullPath(args[0]);
string? outPath = null;
for (var i = 1; i < args.Length; i++)
{
    if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        outPath = Path.GetFullPath(args[++i]);
    }
}

outPath ??= Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "godot-tinytown", "tinytown-atlas-preview.png");
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

var loadResult = SpriteAtlasTomlLoader.LoadFile(tomlPath);
if (!loadResult.Success || loadResult.Asset is null)
{
    Console.Error.WriteLine("Sprite atlas TOML load failed:");
    Console.Error.WriteLine(AssetDiagnosticFormatter.FormatMany(loadResult.Diagnostics));
    return 2;
}

var asset = loadResult.Asset;
using var source = new Bitmap(asset.ResolvedImagePath);
using var preview = new Bitmap(source.Width, source.Height);
using var graphics = Graphics.FromImage(preview);
using var gridPen = new Pen(Color.FromArgb(220, 0, 220, 255), 1f);
using var labelBrush = new SolidBrush(Color.FromArgb(255, 255, 247, 214));
using var shadowBrush = new SolidBrush(Color.FromArgb(180, 10, 14, 20));
using var entityBrush = new SolidBrush(Color.FromArgb(255, 255, 235, 140));
using var pivotBrush = new SolidBrush(Color.FromArgb(255, 255, 84, 84));
using var correctionPen = new Pen(Color.FromArgb(220, 255, 84, 84), 2f);
using var font = new Font("Consolas", 14, FontStyle.Bold);
using var smallFont = new Font("Consolas", 10, FontStyle.Regular);

graphics.DrawImage(source, 0, 0, source.Width, source.Height);
graphics.SmoothingMode = SmoothingMode.AntiAlias;
graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

for (var col = 0; col <= asset.Grid.Columns; col++)
{
    var x = col * asset.Grid.CellWidth;
    graphics.DrawLine(gridPen, x, 0, x, source.Height);
    if (col < asset.Grid.Columns)
    {
        DrawLabel(graphics, smallFont, labelBrush, shadowBrush, $"c{col}", x + 4, 4);
    }
}

for (var row = 0; row <= asset.Grid.Rows; row++)
{
    var y = row * asset.Grid.CellHeight;
    graphics.DrawLine(gridPen, 0, y, source.Width, y);
    if (row < asset.Grid.Rows)
    {
        DrawLabel(graphics, smallFont, labelBrush, shadowBrush, $"r{row}", 4, y + 18);
    }
}

foreach (var (entityId, entity) in asset.Entities.OrderBy(x => x.Key, StringComparer.Ordinal))
{
    var frame = entity.StaticFrame
        ?? entity.Animations.OrderBy(x => x.Key, StringComparer.Ordinal).SelectMany(x => x.Value.Frames).FirstOrDefault();
    if (frame is null)
    {
        continue;
    }

    var cellX = frame.Col * asset.Grid.CellWidth;
    var cellY = frame.Row * asset.Grid.CellHeight;
    DrawLabel(graphics, font, entityBrush, shadowBrush, entityId, cellX + 8, cellY + 28);

    var correction = frame.Correction;
    var totalOffset = entity.Offset + (correction?.Offset ?? Godot.Vector2.Zero);
    var hasCorrection = entity.Scale != 1f
        || entity.Offset != Godot.Vector2.Zero
        || entity.Pivot.HasValue
        || correction is not null;
    if (!hasCorrection)
    {
        continue;
    }

    var centerX = cellX + (asset.Grid.CellWidth / 2f);
    var centerY = cellY + (asset.Grid.CellHeight / 2f);
    graphics.DrawLine(correctionPen, centerX, centerY, centerX + totalOffset.X, centerY + totalOffset.Y);
    graphics.FillEllipse(pivotBrush, centerX + totalOffset.X - 4f, centerY + totalOffset.Y - 4f, 8f, 8f);
}

preview.Save(outPath, ImageFormat.Png);
Console.WriteLine($"Preview written to {outPath}");
return 0;

static void DrawLabel(Graphics graphics, Font font, Brush brush, Brush shadowBrush, string text, float x, float y)
{
    graphics.DrawString(text, font, shadowBrush, x + 1f, y + 1f);
    graphics.DrawString(text, font, brush, x, y);
}
