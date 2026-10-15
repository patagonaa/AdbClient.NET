
public record AdbBitmap(AdbBitmapColorSpace ColorSpace, uint Width, uint Height, AdbBitmapPixelFormat PixelFormat, byte[] Data);

public enum AdbBitmapColorSpace
{
    Unknown = 0,
    SRGB = 1,
    DisplayP3 = 2
}

public record AdbBitmapPixelFormat(
    uint BitsPerPixel,
    uint RedOffset, uint RedLength,
    uint GreenOffset, uint GreenLength,
    uint BlueOffset, uint BlueLength,
    uint AlphaOffset, uint AlphaLength
    );