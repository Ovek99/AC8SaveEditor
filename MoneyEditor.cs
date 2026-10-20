using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Windows.Forms;

public sealed class Campaign
{
    public byte[] Bytes;
    public int MoneyOffset, ChecksumOffset, PackedOffset, PackedLength;
    public ulong Money { get { return BitConverter.ToUInt64(Bytes, MoneyOffset); } }
    static byte[] Text(string s) { return Encoding.ASCII.GetBytes(s); }
    static byte[] FString(string s) { var a=Text(s+"\0"); var b=new byte[a.Length+4]; Buffer.BlockCopy(BitConverter.GetBytes(a.Length),0,b,0,4); Buffer.BlockCopy(a,0,b,4,a.Length); return b; }
    static byte[] Join(params byte[][] parts) { using(var m=new MemoryStream()) { foreach(var p in parts)m.Write(p,0,p.Length); return m.ToArray(); } }
    static int Find(byte[] data, byte[] pattern) {
        int found=-1;
        for(int i=0;i<=data.Length-pattern.Length;i++) { int j=0; while(j<pattern.Length && data[i+j]==pattern[j])j++; if(j==pattern.Length) { if(found>=0)throw new InvalidDataException("Ambiguous save property. This save version is unsupported."); found=i; } }
        if(found<0)throw new InvalidDataException("Required save property missing. This save version is unsupported."); return found;
    }
    static int Scalar(byte[] b,string name,string type,int size) {
        var p=Join(FString(name),FString(type),new byte[4],BitConverter.GetBytes(size),new byte[1]); return Find(b,p)+p.Length;
    }
    public static uint Crc(byte[] b,int offset,int count,uint seed) {
        uint c=~seed; for(int i=offset;i<offset+count;i++) { c^=b[i]; for(int k=0;k<8;k++)c=(c&1)!=0 ? (c>>1)^0xedb88320u : c>>1; } return ~c;
    }
    public uint Checksum() { return Crc(Bytes,PackedOffset,PackedLength,0x41916ebdu); }
    public static Campaign Parse(byte[] bytes) {
        if(bytes.Length<100 || Encoding.ASCII.GetString(bytes,0,4)!="GVAS")throw new InvalidDataException("This is not an Unreal GVAS save.");
        Find(bytes,FString("/Script/Live.LiveCampaignSaveGame"));
        var c=new Campaign(); c.Bytes=(byte[])bytes.Clone();
        c.MoneyOffset=Scalar(bytes,"CurrentMRP","UInt64Property",8);
        c.ChecksumOffset=Scalar(bytes,"Checksum","UInt32Property",4);
        var p=Join(FString("PackedData"),FString("ArrayProperty"),BitConverter.GetBytes(1),FString("ByteProperty"),new byte[4]);
        int pos=Find(bytes,p)+p.Length;
        uint size=BitConverter.ToUInt32(bytes,pos); pos+=4;
        if(bytes[pos++]!=0)throw new InvalidDataException("Unsupported PackedData GUID.");
        c.PackedLength=BitConverter.ToInt32(bytes,pos); c.PackedOffset=pos+4;
        if(c.PackedLength<0 || size!=(long)c.PackedLength+4 || (long)c.PackedOffset+c.PackedLength>bytes.Length || c.MoneyOffset<c.PackedOffset || (long)c.MoneyOffset+8>c.PackedOffset+c.PackedLength || c.ChecksumOffset<c.PackedOffset+c.PackedLength || c.ChecksumOffset+4>bytes.Length)throw new InvalidDataException("Invalid save boundaries.");
        if(c.Checksum()!=BitConverter.ToUInt32(bytes,c.ChecksumOffset))throw new InvalidDataException("Checksum validation failed. No changes were made.");
        return c;
    }
    public byte[] Edit(ulong value) {
        var c=Parse(Bytes); Buffer.BlockCopy(BitConverter.GetBytes(value),0,c.Bytes,c.MoneyOffset,8);
        Buffer.BlockCopy(BitConverter.GetBytes(c.Checksum()),0,c.Bytes,c.ChecksumOffset,4);
        if(Parse(c.Bytes).Money!=value)throw new InvalidDataException("Verification failed."); return c.Bytes;
    }
    public static string Write(string path,byte[] data) {
        Parse(data); path=Path.GetFullPath(path); string backup=null;
        string tmp=Path.Combine(Path.GetDirectoryName(path),"."+Guid.NewGuid().ToString("N")+".tmp");
        try {
            File.WriteAllBytes(tmp,data);
            if(File.Exists(path)) { backup=path+"."+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"."+Guid.NewGuid().ToString("N").Substring(0,6)+".bak"; File.Replace(tmp,path,backup); }
            else File.Move(tmp,path);
            return backup;
        } finally { if(File.Exists(tmp))File.Delete(tmp); }
    }
}
