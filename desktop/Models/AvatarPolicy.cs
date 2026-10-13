namespace QraConfig.Models;

public static class AvatarPolicy
{
    public static bool ValidJpeg(ReadOnlySpan<byte> data)
    {
        if(data.Length is <4 or >24576 || data[0]!=255 || data[1]!=216 || data[^2]!=255 || data[^1]!=217) return false;
        var pos=2; var dimensions=false;
        while(pos<data.Length-2) {
            if(data[pos++]!=255) return false;
            while(pos<data.Length && data[pos]==255) pos++;
            if(pos>=data.Length) return false; var marker=data[pos++];
            if(marker==0xe1) return false; // no EXIF or XMP
            if(marker==0xda) return dimensions;
            if(marker==0xd9) return dimensions;
            if(marker is 0x01 or >=0xd0 and <=0xd7) continue;
            if(pos+2>data.Length) return false; var length=(data[pos]<<8)|data[pos+1];
            if(length<2 || pos+length>data.Length) return false;
            if(marker is 0xc0 or 0xc1 or 0xc2) { if(length<8 || ((data[pos+3]<<8)|data[pos+4])!=96 || ((data[pos+5]<<8)|data[pos+6])!=96) return false; dimensions=true; }
            pos+=length;
        }
        return false;
    }
}
