using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QraConfig.Models;

namespace QraConfig.Services;

public static class AvatarService
{
    public static string EncodePhoto(string path)
    {
        if(new FileInfo(path).Length>10*1024*1024) throw new InvalidOperationException("Фото должно быть не больше 10 МиБ.");
        using var input=File.OpenRead(path);
        var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad);
        BitmapSource source=decoder.Frames[0];
        if(decoder.Frames[0].Metadata is BitmapMetadata metadata) {
            object? orientation=null; try { orientation=metadata.GetQuery("/app1/ifd/{ushort=274}"); } catch {}
            var transform=new TransformGroup();
            switch(orientation is null?1:Convert.ToInt32(orientation)) {
                case 2: transform.Children.Add(new ScaleTransform(-1,1)); break;
                case 3: transform.Children.Add(new RotateTransform(180)); break;
                case 4: transform.Children.Add(new ScaleTransform(1,-1)); break;
                case 5: transform.Children.Add(new ScaleTransform(-1,1)); transform.Children.Add(new RotateTransform(270)); break;
                case 6: transform.Children.Add(new RotateTransform(90)); break;
                case 7: transform.Children.Add(new ScaleTransform(-1,1)); transform.Children.Add(new RotateTransform(90)); break;
                case 8: transform.Children.Add(new RotateTransform(270)); break;
            }
            if(transform.Children.Count>0) source=new TransformedBitmap(source,transform);
        } if(source.PixelWidth>16384 || source.PixelHeight>16384) throw new InvalidOperationException("Размер фото превышает 16384 пикселя."); var size=Math.Min(source.PixelWidth,source.PixelHeight);
        var crop=new CroppedBitmap(source,new Int32Rect((source.PixelWidth-size)/2,(source.PixelHeight-size)/2,size,size));
        var visual=new DrawingVisual(); using(var drawing=visual.RenderOpen()) { drawing.DrawRectangle(Brushes.Black,null,new Rect(0,0,96,96)); drawing.DrawImage(crop,new Rect(0,0,96,96)); }
        var bitmap=new RenderTargetBitmap(96,96,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder=new JpegBitmapEncoder { QualityLevel=85 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output=new MemoryStream(); encoder.Save(output);
        var bytes=output.ToArray(); if(!AvatarPolicy.ValidJpeg(bytes)) throw new InvalidOperationException("Не удалось подготовить фото 96×96 JPEG.");
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
