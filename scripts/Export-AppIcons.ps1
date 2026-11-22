# Rebuild the Windows icon assets from the approved transparent source.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not ('WinYandexIconExporter' -as [type])) {
    Add-Type -ReferencedAssemblies @(
        'System.Drawing.Common', 'System.Drawing.Primitives',
        'System.Private.Windows.GdiPlus', 'System.Private.Windows.Core', 'System.Runtime'
    ) -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class WinYandexIconExporter
{
    public static byte[] Render(Image source, int width, int height)
    {
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        using (var attributes = new ImageAttributes())
        using (var stream = new MemoryStream())
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            int edge = Math.Min(width, height);
            var destination = new Rectangle((width - edge) / 2, (height - edge) / 2, edge, edge);
            graphics.DrawImage(source, destination, 0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel, attributes);
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    public static void WriteIco(Image source, string path)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var frames = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) frames[i] = Render(source, sizes[i], sizes[i]);
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(frames[i].Length);
                writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
    }
}
'@
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $repoRoot 'src/YMusic.WinUI/Assets'
$source = [Drawing.Image]::FromFile((Join-Path $repoRoot 'assets/app-icon-source.png'))
try {
    $outputs = @{
        'Square44x44Logo.png' = @(44, 44)
        'Square44x44Logo.scale-200.png' = @(88, 88)
        'Square44x44Logo.targetsize-16_altform-unplated.png' = @(16, 16)
        'Square44x44Logo.targetsize-24_altform-unplated.png' = @(24, 24)
        'Square44x44Logo.targetsize-32_altform-unplated.png' = @(32, 32)
        'Square44x44Logo.targetsize-48_altform-unplated.png' = @(48, 48)
        'Square44x44Logo.targetsize-48_altform-lightunplated.png' = @(48, 48)
        'Square44x44Logo.targetsize-64_altform-unplated.png' = @(64, 64)
        'Square44x44Logo.targetsize-256_altform-unplated.png' = @(256, 256)
        'Square150x150Logo.png' = @(150, 150)
        'Square150x150Logo.scale-200.png' = @(300, 300)
        'StoreLogo.png' = @(50, 50)
        'LockScreenLogo.scale-200.png' = @(48, 48)
        'Wide310x150Logo.scale-200.png' = @(620, 300)
        'SplashScreen.scale-200.png' = @(1240, 600)
    }
    foreach ($entry in $outputs.GetEnumerator()) {
        [IO.File]::WriteAllBytes((Join-Path $assetRoot $entry.Key),
            [WinYandexIconExporter]::Render($source, $entry.Value[0], $entry.Value[1]))
    }
    [WinYandexIconExporter]::WriteIco($source, (Join-Path $assetRoot 'AppIcon.ico'))
    [IO.File]::WriteAllBytes((Join-Path $repoRoot 'assets/logo.png'),
        [WinYandexIconExporter]::Render($source, 300, 300))
    Write-Output 'Exported 16 PNG assets and ICO with 10 sizes (16-256 px).'
}
finally {
    $source.Dispose()
}
