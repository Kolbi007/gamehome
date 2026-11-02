// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 The GameHome contributors

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace GameHome.App;

/// <summary>
/// Nedskalerte kopier av spillbildene på disk (appens LocalCache, som Windows kan tømme). Steams bakgrunnsbilder er
/// 3840 px brede og flere MB; å dekode dem på nytt for hver flis koster minne og tid. Kopien lages én gang i den
/// bredden den vises i, og senere lastes bare den lille filen. Navnet inneholder kildens sti, endringstid og bredde,
/// så et nytt bilde gir en ny kopi.
/// </summary>
internal static class ThumbnailCache
{
    private static readonly SemaphoreSlim Gate = new(2, 2);

    private static string Folder => Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "thumbs");

    /// <summary>Sti til en kopi som er høyst <paramref name="width"/> px bred, eller originalen hvis den er mindre.</summary>
    public static async Task<string> GetAsync(string source, int width)
    {
        var info = new FileInfo(source);
        if (!info.Exists)
        {
            return source;
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{source}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{width}"))))[..24];
        var target = Path.Combine(Folder, key + ".jpg");
        var png = Path.ChangeExtension(target, ".png");
        if (File.Exists(target) || File.Exists(png))
        {
            return File.Exists(target) ? target : png;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(target) || File.Exists(png))
            {
                return File.Exists(target) ? target : png;
            }

            Directory.CreateDirectory(Folder);
            using (var input = File.OpenRead(source))
            {
                var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
                if (decoder.PixelWidth <= width * 1.15)
                {
                    return source; // Allerede liten nok; ingen kopi.
                }

                var height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * (double)width / decoder.PixelWidth));
                var pixels = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                // Logoer har gjennomsiktighet og lagres som PNG; bilder som JPEG (mye mindre).
                var hasAlpha = decoder.BitmapAlphaMode != BitmapAlphaMode.Ignore
                    && source.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                if (hasAlpha)
                {
                    target = Path.ChangeExtension(target, ".png");
                }

                var temp = target + ".tmp";
                using (var output = File.Create(temp))
                {
                    var encoder = await BitmapEncoder.CreateAsync(
                        hasAlpha ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId, output.AsRandomAccessStream());
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, height, 96, 96,
                        pixels.DetachPixelData());
                    await encoder.FlushAsync();
                }

                File.Move(temp, target, overwrite: true);
            }

            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return source; // Kopien er bare en optimalisering.
        }
        finally
        {
            Gate.Release();
        }
    }
}
