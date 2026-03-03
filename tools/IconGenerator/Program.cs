using SkiaSharp;
using Svg.Skia;

// Usage: IconGenerator <svg-path> <output-png-path> [width] [height]
var svgPath = args.Length > 0 ? args[0] : throw new ArgumentException("Usage: IconGenerator <svg> <out.png> [w] [h]");
var outputPath = args.Length > 1 ? args[1] : throw new ArgumentException("Usage: IconGenerator <svg> <out.png> [w] [h]");
var width = args.Length > 2 ? int.Parse(args[2]) : 512;
var height = args.Length > 3 ? int.Parse(args[3]) : width;

using var svg = new SKSvg();
svg.Load(svgPath);

if (svg.Picture == null)
{
    Console.Error.WriteLine("Failed to load SVG.");
    return 1;
}

var bounds = svg.Picture.CullRect;
float scaleX = width / bounds.Width;
float scaleY = height / bounds.Height;

using var bitmap = new SKBitmap(width, height);
using var canvas = new SKCanvas(bitmap);
canvas.Clear(SKColors.Transparent);
canvas.Scale(scaleX, scaleY);
canvas.DrawPicture(svg.Picture);
canvas.Flush();

using var image = SKImage.FromBitmap(bitmap);
using var data = image.Encode(SKEncodedImageFormat.Png, 100);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using var stream = File.Create(outputPath);
data.SaveTo(stream);

var fi = new FileInfo(outputPath);
Console.WriteLine($"Created: {outputPath}");
Console.WriteLine($"Size: {fi.Length / 1024.0:F1} KB  ({width}x{height})");
return 0;
