using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Reflection;

public sealed class UnlockItem
{
    public string Key,Category,Id,Name,Aircraft,Tags,Description,PlaneCategory,Field,Mirror;
    public uint PlaneId,DefaultSkin,Reference;
    public int Bit;
    public uint[] Rules;
    public string[] Weapons;
}
public sealed class Catalogue
{
    public List<UnlockItem> Items;
    public List<string> WeaponOrder;
    public static Catalogue Load() {
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Catalogue"))
        using(var reader=new StreamReader(stream,Encoding.UTF8)) {
            var json=new JavaScriptSerializer { MaxJsonLength=8000000 };
            var c=json.Deserialize<Catalogue>(reader.ReadToEnd());
            if(c.Items.Count==0 || c.Items.Select(x=>x.Key).Distinct().Count()!=c.Items.Count)throw new InvalidDataException("Invalid embedded catalogue.");
            return c;
        }
    }
}

// Keeps all untouched tag/payload bytes. Only the payload and size of edited
// properties are rebuilt; arbitrary unknown properties remain opaque.
public sealed class SaveReader
{
    public readonly byte[] Bytes; public int Pos;
    public SaveReader(byte[] bytes) { Bytes=bytes; }
    public byte[] Take(int count) {
        if(count<0 || (long)Pos+count>Bytes.Length)throw new InvalidDataException("Truncated save property.");
        var result=new byte[count];Buffer.BlockCopy(Bytes,Pos,result,0,count);Pos+=count;return result;
    }
    public int Int() { return BitConverter.ToInt32(Take(4),0); }
    public string String() {
        int length=Int();if(length==0)return "";
        if(length==int.MinValue || Math.Abs((long)length)>1000000)throw new InvalidDataException("Invalid save string.");
        var data=Take(length>0?length:checked(-length*2));int nul=length>0?1:2;
        if(data[data.Length-1]!=0 || (nul==2 && data[data.Length-2]!=0))throw new InvalidDataException("Unterminated save string.");
        return (length>0?Encoding.UTF8:Encoding.Unicode).GetString(data,0,data.Length-nul);
    }
    public SaveType Type(int depth) {
        if(depth>16)throw new InvalidDataException("Invalid type nesting.");
        var t=new SaveType { Name=String() };int n=Int();if(n<0 || n>16)throw new InvalidDataException("Invalid type arguments.");
        for(int i=0;i<n;i++)t.Inner.Add(Type(depth+1));return t;
    }
    public TagList List() {
        var list=new TagList();
        while(true) {
            int start=Pos;string name=String();
            if(name=="None") { list.Terminator=Slice(start,Pos-start);return list; }
            var type=Type(0);int sizeAt=Pos;int size=Int();int flagsAt=Pos;byte flags=Take(1)[0];
            if((flags&4)!=0)throw new InvalidDataException("Unsupported property extensions.");
            if((flags&1)!=0)Take(4);if((flags&2)!=0)Take(16);
            var tag=new SaveTag { Name=name,Type=type,Prefix=Slice(start,sizeAt-start),Suffix=Slice(flagsAt,Pos-flagsAt),Data=Take(size) };
            list.Tags.Add(tag);
        }
    }
    byte[] Slice(int start,int size) { var data=new byte[size];Buffer.BlockCopy(Bytes,start,data,0,size);return data; }
    public static byte[] FString(string text) { var bytes=Encoding.UTF8.GetBytes(text+"\0");return Binary.Join(BitConverter.GetBytes(bytes.Length),bytes); }
}
public sealed class SaveType { public string Name; public List<SaveType> Inner=new List<SaveType>(); }
public static class Binary
{
    public static byte[] Join(params byte[][] parts) { using(var m=new MemoryStream()) { foreach(var p in parts)m.Write(p,0,p.Length);return m.ToArray(); } }
    public static bool Equal(byte[] a,byte[] b) { return a.SequenceEqual(b); }
    public static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
}
public sealed class TagList
{
    public List<SaveTag> Tags=new List<SaveTag>(); public byte[] Terminator;
    public SaveTag Get(string name) { var matches=Tags.Where(x=>x.Name==name).ToList();Binary.Require(matches.Count==1,"Required property is missing or ambiguous: "+name);return matches[0]; }
    public byte[] Build() { return Binary.Join(Tags.Select(x=>x.Build()).Concat(new[]{Terminator}).ToArray()); }
    public TagList Clone() { return new SaveReader(Build()).List(); }
}
public sealed class SaveTag
{
    public string Name; public SaveType Type; public byte[] Prefix,Suffix,Data;
    public byte[] Build() { return Binary.Join(Prefix,BitConverter.GetBytes(Data.Length),Suffix,Data); }
    public uint Number() { Binary.Require(Data.Length==4,"Expected 32-bit field: "+Name);return BitConverter.ToUInt32(Data,0); }
    public void Number(uint value) { Binary.Require(Data.Length==4,"Expected 32-bit field: "+Name);Data=BitConverter.GetBytes(value); }
    public string StringValue() { var r=new SaveReader(Data);var s=r.String();Binary.Require(r.Pos==Data.Length,"Unexpected enum payload: "+Name);return s; }
    public List<TagList> Structs() {
        Binary.Require(Type.Name=="ArrayProperty" && Type.Inner[0].Name=="StructProperty","Expected struct array: "+Name);
        var r=new SaveReader(Data);int n=r.Int();Binary.Require(n>=0 && n<=100000,"Invalid struct count.");var result=new List<TagList>();
        for(int i=0;i<n;i++)result.Add(r.List());Binary.Require(r.Pos==Data.Length,"Unexpected bytes after "+Name);return result;
    }
    public void Structs(List<TagList> value) { Data=Binary.Join(new[]{BitConverter.GetBytes(value.Count)}.Concat(value.Select(x=>x.Build())).ToArray()); }
    public List<KeyValuePair<string,byte[]>> Values() {
        Binary.Require(Type.Name=="ArrayProperty" && Type.Inner.Count==1,"Expected array: "+Name);
        string kind=Type.Inner[0].Name;var r=new SaveReader(Data);int count=r.Int();Binary.Require(count>=0 && count<=1000000,"Invalid array length.");
        var result=new List<KeyValuePair<string,byte[]>>();
        for(int i=0;i<count;i++) {
            int start=r.Pos;string value;
            if(kind=="IntProperty" || kind=="UInt32Property")value=unchecked((uint)r.Int()).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else if(kind=="EnumProperty" || kind=="NameProperty" || kind=="StrProperty")value=r.String();
            else throw new InvalidDataException("Unsupported unlock array: "+Name);
            var raw=new byte[r.Pos-start];Buffer.BlockCopy(Data,start,raw,0,raw.Length);result.Add(new KeyValuePair<string,byte[]>(value,raw));
        }
        Binary.Require(r.Pos==Data.Length,"Unexpected bytes after "+Name);return result;
    }
    public bool Contains(string id) { return Values().Any(x=>x.Key==id); }
    public bool Remove(string id) {
        var values=Values();int removed=values.RemoveAll(x=>x.Key==id);if(removed==0)return false;
        Data=Binary.Join(new[]{BitConverter.GetBytes(values.Count)}.Concat(values.Select(x=>x.Value)).ToArray());return true;
    }
    public void ReplaceValues(Func<string,string> replace) {
        var values=Values();bool changed=false;var result=new List<byte[]>();string kind=Type.Inner[0].Name;
        foreach(var value in values) {
            string next=replace(value.Key);if(next==value.Key)result.Add(value.Value);
            else { changed=true;if(next!=null)result.Add(kind=="IntProperty" || kind=="UInt32Property"?BitConverter.GetBytes(uint.Parse(next)):SaveReader.FString(next)); }
        }
        if(changed)Data=Binary.Join(new[]{BitConverter.GetBytes(result.Count)}.Concat(result).ToArray());
    }
    public bool Add(string id,List<string> order) {
        var values=Values();if(values.Any(x=>x.Key==id))return false;
        string kind=Type.Inner[0].Name;byte[] raw=(kind=="IntProperty" || kind=="UInt32Property")?BitConverter.GetBytes(uint.Parse(id,System.Globalization.CultureInfo.InvariantCulture)):SaveReader.FString(id);
        int at=values.Count;
        if(order!=null) { int rank=order.IndexOf(id);Binary.Require(rank>=0,"Unknown weapon ordering: "+id);int first=values.FindIndex(x=>order.IndexOf(x.Key)>rank);if(first>=0)at=first; }
        values.Insert(at,new KeyValuePair<string,byte[]>(id,raw));Data=Binary.Join(new[]{BitConverter.GetBytes(values.Count)}.Concat(values.Select(x=>x.Value)).ToArray());return true;
    }
}

public sealed class UnlockDocument
{
    readonly Campaign source; readonly Catalogue catalogue;
    public readonly List<TagList> Sections=new List<TagList>();
    TagList music;
    List<TagList> aircraftRecords,unlockRecords;
    public UnlockDocument(Campaign campaign,Catalogue data) {
        source=campaign;catalogue=data;
        var packed=new byte[campaign.PackedLength];Buffer.BlockCopy(campaign.Bytes,campaign.PackedOffset,packed,0,packed.Length);
        var r=new SaveReader(packed);while(r.Pos<packed.Length)Sections.Add(r.List());
        Binary.Require(Binary.Equal(packed,BuildPacked()),"Save did not round-trip exactly. Editing refused.");
    }
    public SaveTag Root(string name) { var matches=Sections.SelectMany(x=>x.Tags).Where(x=>x.Name==name).ToList();Binary.Require(matches.Count==1,"Required property is missing or ambiguous: "+name);return matches[0]; }
    public SaveTag Field(string name) {
        if(name=="OwnedMusicList" || name=="NewlyOwnedMusicList") {
            if(music==null) { var r=new SaveReader(Root("MusicPlayer").Data);music=r.List();Binary.Require(r.Pos==r.Bytes.Length,"Unexpected MusicPlayer payload."); }
            return music.Get(name);
        }
        return Root(name);
    }
    public List<TagList> AircraftRecords { get { return aircraftRecords ?? (aircraftRecords=Root("AircraftTypeRecords").Structs()); } }
    public List<TagList> UnlockRecords { get { return unlockRecords ?? (unlockRecords=Root("UnlockData").Structs()); } }
    public List<KeyValuePair<uint,byte>> Aircraft() {
        var tag=Root("OwnedAircrafts");Binary.Require(tag.Type.Name=="MapProperty" && tag.Type.Inner.Count==2 && tag.Type.Inner[0].Name=="UInt32Property" && tag.Type.Inner[1].Name=="ByteProperty","Unsupported aircraft map.");
        var r=new SaveReader(tag.Data);Binary.Require(r.Int()==0,"Unsupported removed aircraft entries.");int count=r.Int();Binary.Require(count>=0 && count<100000,"Invalid aircraft count.");
        var result=new List<KeyValuePair<uint,byte>>();for(int i=0;i<count;i++)result.Add(new KeyValuePair<uint,byte>(unchecked((uint)r.Int()),r.Take(1)[0]));Binary.Require(r.Pos==r.Bytes.Length,"Invalid aircraft map length.");return result;
    }
    TagList Record(uint plane) { return AircraftRecords.FirstOrDefault(x=>x.Get("PlaneID").Number()==plane); }
    public bool Owned(UnlockItem item) {
        if(item.Category=="aircraft")return Aircraft().Any(x=>x.Key==item.PlaneId && x.Value>0);
        if(item.Category=="weapons") { var rec=Record(item.PlaneId);return rec!=null && rec.Get("OwnedWeapons").Contains(item.Weapons[0]); }
        if(item.Category=="feature")return (Root("FeatureFlagMask").Number()&(1u<<item.Bit))!=0;
        return Field(item.Field).Contains(item.Id);
    }
    void AddPair(string field,string mirror,string id) { Field(field).Add(id,null);if(!string.IsNullOrEmpty(mirror))Field(mirror).Add(id,null); }
    void AddTree(uint id) { AddPair("UnlockedAircraftTreeNodeIDs","NewlyUnlockedAircraftTreeNodeIDs",id.ToString()); }
    TagList EnsureRecord(uint plane,string category) {
        var existing=Record(plane);if(existing!=null)return existing;
        Binary.Require(AircraftRecords.Count>0,"Cannot create an aircraft record without a template.");
        var copy=AircraftRecords[0].Clone();
        foreach(var p in copy.Tags) {
            if(p.Name=="PlaneID")p.Number(plane);
            else if(p.Name=="PlaneCategory")p.Data=SaveReader.FString(category);
            else if(p.Type.Name=="ArrayProperty")p.Data=BitConverter.GetBytes(0);
            else if(p.Type.Name=="MapProperty" || p.Type.Name=="SetProperty")p.Data=new byte[8];
            else if(new[]{"IntProperty","UInt32Property","UInt64Property","Int64Property","FloatProperty","DoubleProperty","Int16Property","UInt16Property","ByteProperty"}.Contains(p.Type.Name))p.Data=new byte[p.Data.Length];
        }
        AircraftRecords.Add(copy);return copy;
    }
    void AddWeapon(uint plane,string category,string weapon) {
        var record=EnsureRecord(plane,category);
        record.Get("OwnedWeapons").Add(weapon,catalogue.WeaponOrder);record.Get("NewlyOwnedWeapons").Add(weapon,catalogue.WeaponOrder);
    }
    void AddAircraft(UnlockItem item) {
        var owned=Aircraft();
        if(!owned.Any(x=>x.Key==item.PlaneId && x.Value>0)) {
            int at=owned.FindIndex(x=>x.Key==item.PlaneId);var value=new KeyValuePair<uint,byte>(item.PlaneId,4);if(at<0)owned.Add(value);else owned[at]=value;
            Root("OwnedAircrafts").Data=Binary.Join(new[]{new byte[4],BitConverter.GetBytes(owned.Count)}.Concat(owned.Select(x=>Binary.Join(BitConverter.GetBytes(x.Key),new[]{x.Value}))).ToArray());
            AddPair("NewlyOwnedAircrafts",null,item.PlaneId.ToString());
        }
        EnsureRecord(item.PlaneId,item.PlaneCategory);AddTree(item.PlaneId);
        if(item.DefaultSkin!=0)AddPair("UnlockedSkinIdList","NewlyUnlockedSkinIdList",item.DefaultSkin.ToString());
        if(item.Weapons.Length>0)AddWeapon(item.PlaneId,item.PlaneCategory,item.Weapons[0]);
        Ack(item);
    }
    void Ack(UnlockItem item) { Ack(item,true); }
    void Ack(UnlockItem item,bool activated) {
        foreach(uint id in item.Rules) {
            var matches=UnlockRecords.Where(x=>x.Get("ID").Number()==id).ToList();Binary.Require(matches.Count==1,"Missing unlock tracking rule "+id+". Save version may differ.");
            var flag=matches[0].Get("bIsActivated");Binary.Require(flag.Type.Name=="BoolProperty" && flag.Data.Length==0,"Unsupported unlock flag.");
            if(activated)flag.Suffix[0]|=16;else flag.Suffix[0]&=239;
        }
    }
    public void Grant(UnlockItem item) {
        if(item.Category=="aircraft")AddAircraft(item);
        else if(item.Category=="weapons") {
            var plane=catalogue.Items.Single(x=>x.Category=="aircraft" && x.PlaneId==item.PlaneId);AddAircraft(plane);
            AddWeapon(item.PlaneId,item.PlaneCategory,item.Weapons[0]);
        }
        else if(item.Category=="feature") { var p=Root("FeatureFlagMask");p.Number(p.Number()|(1u<<item.Bit)); }
        else {
            AddPair(item.Field,item.Mirror,item.Id);
            if(item.Category=="parts")AddTree(uint.Parse(item.Id));
        }
        Ack(item);
    }
    void RemovePair(string field,string mirror,string id) { Field(field).Remove(id);if(!string.IsNullOrEmpty(mirror))Field(mirror).Remove(id); }
    void RemoveTree(uint id) {
        RemovePair("UnlockedAircraftTreeNodeIDs","NewlyUnlockedAircraftTreeNodeIDs",id.ToString());
        Root("NewlyIconUnlockedAircraftTreeNodeIDs").Remove(id.ToString());
    }
    void RevokeOwnership(UnlockItem item) {
        if(item.Category=="aircraft") {
            var owned=Aircraft();owned.RemoveAll(x=>x.Key==item.PlaneId);
            Root("OwnedAircrafts").Data=Binary.Join(new[]{new byte[4],BitConverter.GetBytes(owned.Count)}.Concat(owned.Select(x=>Binary.Join(BitConverter.GetBytes(x.Key),new[]{x.Value}))).ToArray());
            Root("NewlyOwnedAircrafts").Remove(item.PlaneId.ToString());RemoveTree(item.PlaneId);
            var rec=Record(item.PlaneId);
            if(rec!=null) { rec.Get("OwnedWeapons").Data=BitConverter.GetBytes(0);rec.Get("NewlyOwnedWeapons").Data=BitConverter.GetBytes(0); }
        }
        else if(item.Category=="weapons") {
            var rec=Record(item.PlaneId);if(rec!=null) {rec.Get("OwnedWeapons").Remove(item.Weapons[0]);rec.Get("NewlyOwnedWeapons").Remove(item.Weapons[0]);}
        }
        else if(item.Category=="feature") {
            var p=Root("FeatureFlagMask");p.Number(p.Number()&~(1u<<item.Bit));
            if(item.Id=="AceDifficulty" && Root("SavedDifficulty").StringValue()=="ELiveDifficulty::Ace")Root("SavedDifficulty").Data=SaveReader.FString("ELiveDifficulty::Normal");
        }
        else { RemovePair(item.Field,item.Mirror,item.Id);if(item.Category=="parts")RemoveTree(uint.Parse(item.Id)); }
        Ack(item,false);
    }
    Dictionary<string,uint> PlaneMap(string field,HashSet<uint> removed,uint fallback,bool update) {
        var tag=Root(field);Binary.Require(tag.Type.Name=="MapProperty" && tag.Type.Inner[0].Name=="EnumProperty" && tag.Type.Inner[1].Name=="IntProperty","Unsupported sortie plane map.");
        var r=new SaveReader(tag.Data);Binary.Require(r.Int()==0,"Unsupported removed sortie map entries.");int n=r.Int();Binary.Require(n>=0 && n<100000,"Invalid sortie map count.");
        var result=new Dictionary<string,uint>();var bytes=(byte[])tag.Data.Clone();
        for(int i=0;i<n;i++) {string pilot=r.String();int offset=r.Pos;uint plane=unchecked((uint)r.Int());result.Add(pilot,plane);if(update && removed.Contains(plane))Buffer.BlockCopy(BitConverter.GetBytes(fallback),0,bytes,offset,4);}
        Binary.Require(r.Pos==r.Bytes.Length,"Invalid sortie map payload.");if(update)tag.Data=bytes;return result;
    }
    void CleanEquipment(List<UnlockItem> removed) {
        var planes=new HashSet<uint>(removed.Where(x=>x.Category=="aircraft").Select(x=>x.PlaneId));
        var skins=new HashSet<string>(removed.Where(x=>x.Category=="skin").Select(x=>x.Id));
        var emblems=new HashSet<string>(removed.Where(x=>x.Category=="emblem").Select(x=>x.Id));
        var parts=new HashSet<string>(removed.Where(x=>x.Category=="parts").Select(x=>x.Id));
        var weapons=removed.Where(x=>x.Category=="weapons").GroupBy(x=>x.PlaneId).ToDictionary(x=>x.Key,x=>new HashSet<string>(x.Select(y=>y.Weapons[0])));
        if(planes.Count+skins.Count+emblems.Count+parts.Count+weapons.Count==0)return;
        uint fallback=Aircraft().Where(x=>x.Value>0).Select(x=>x.Key).FirstOrDefault();
        var pilotPlanes=PlaneMap("LastSortiedPlaneIDsByCampaignMission",planes,fallback,planes.Count>0);
        if(planes.Count>0)PlaneMap("LastSortiedPlaneIDs",planes,fallback,true);
        string defaultWeapon="ELiveWeaponID::WID_NONE";var fallbackRecord=Record(fallback);
        if(fallbackRecord!=null)defaultWeapon=fallbackRecord.Get("OwnedWeapons").Values().Select(x=>x.Key).FirstOrDefault() ?? defaultWeapon;
        Action<TagList,uint,bool> clean=(list,plane,reset)=> {
            foreach(var tag in list.Tags) {
                if(tag.Name=="SkinID" && (reset || skins.Contains(tag.Number().ToString())))tag.Number(uint.MaxValue);
                if(tag.Name=="EnabledPartIDs")tag.ReplaceValues(id=>reset || parts.Contains(id)?null:id);
                if(tag.Name=="EmblemIDs")tag.ReplaceValues(id=>emblems.Contains(id)?"0":id);
                if(tag.Name=="SpWeaponIDs") {
                    int slot=0;tag.ReplaceValues(id=>reset?(slot++==0?defaultWeapon:"ELiveWeaponID::WID_NONE"):(planes.Contains(plane) || (weapons.ContainsKey(plane) && weapons[plane].Contains(id))?"ELiveWeaponID::WID_NONE":id));
                }
            }
        };
        foreach(var record in AircraftRecords) {
            uint plane=record.Get("PlaneID").Number();var pilots=record.Get("PilotData").Structs();
            foreach(var pilot in pilots)clean(pilot,plane,false);record.Get("PilotData").Structs(pilots);
        }
        var sets=Root("AircraftSets").Structs();
        foreach(var set in sets) {
            uint plane=set.Get("PlaneID").Number();bool reset=planes.Contains(plane);if(reset)set.Get("PlaneID").Number(fallback);clean(set,plane,reset);
        }
        Root("AircraftSets").Structs(sets);
        var last=Root("LastSortiedAircraftPilotDataByCampaignMission").Structs();
        foreach(var pilot in last) {
            uint plane;bool found=pilotPlanes.TryGetValue(pilot.Get("PilotId").StringValue(),out plane);
            Binary.Require(found || weapons.Count==0,"Cannot identify a saved pilot's aircraft for weapon cleanup.");clean(pilot,plane,planes.Contains(plane));
        }
        Root("LastSortiedAircraftPilotDataByCampaignMission").Structs(last);
    }
    public void ApplyChanges(IEnumerable<UnlockItem> grants,IEnumerable<UnlockItem> relocks) {
        var add=grants.ToList();var remove=relocks.ToList();
        Binary.Require(!add.Select(x=>x.Key).Intersect(remove.Select(x=>x.Key)).Any(),"An item cannot be unlocked and relocked together.");
        var removedPlanes=new HashSet<uint>(remove.Where(x=>x.Category=="aircraft").Select(x=>x.PlaneId));
        Binary.Require(!add.Any(x=>x.Category=="weapons" && removedPlanes.Contains(x.PlaneId)),"A selected weapon needs an aircraft you are relocking. Keep that aircraft unlocked or clear the weapon unlock.");
        var remaining=new HashSet<uint>(Aircraft().Where(x=>x.Value>0).Select(x=>x.Key));
        remaining.UnionWith(add.Where(x=>x.Category=="aircraft" || x.Category=="weapons").Select(x=>x.PlaneId));remaining.ExceptWith(removedPlanes);
        Binary.Require(removedPlanes.Count==0 || remaining.Count>0,"Keep at least one aircraft unlocked before saving.");
        if(remove.Any(x=>x.Category=="hangars")) {
            var hangars=new HashSet<string>(Root("UnlockedHangarIDs").Values().Select(x=>x.Key));hangars.UnionWith(add.Where(x=>x.Category=="hangars").Select(x=>x.Id));hangars.ExceptWith(remove.Where(x=>x.Category=="hangars").Select(x=>x.Id));
            Binary.Require(hangars.Count>0,"Keep at least one hangar unlocked before saving.");
        }
        foreach(var item in add)Grant(item);
        foreach(var item in remove)RevokeOwnership(item);
        var songs=new HashSet<string>(remove.Where(x=>x.Category=="music").Select(x=>x.Id));
        if(songs.Count>0) {
            Field("OwnedMusicList");var playlists=music.Get("Playlists").Structs();
            foreach(var playlist in playlists)foreach(var field in playlist.Tags.Where(x=>x.Type.Name=="ArrayProperty" && x.Type.Inner.Count==1 && new[]{"NameProperty","StrProperty","EnumProperty"}.Contains(x.Type.Inner[0].Name)))field.ReplaceValues(id=>songs.Contains(id)?null:id);
            music.Get("Playlists").Structs(playlists);
        }
        CleanEquipment(remove);
        foreach(var item in add)Binary.Require(Owned(item),"Unlock verification failed: "+item.Name);
        foreach(var item in remove)Binary.Require(!Owned(item),"Relock verification failed: "+item.Name);
    }
    public byte[] BuildPacked() {
        if(music!=null)Root("MusicPlayer").Data=music.Build();
        if(aircraftRecords!=null)Root("AircraftTypeRecords").Structs(aircraftRecords);
        if(unlockRecords!=null)Root("UnlockData").Structs(unlockRecords);
        return Binary.Join(Sections.Select(x=>x.Build()).ToArray());
    }
    public byte[] Export(ulong money) {
        Root("CurrentMRP").Data=BitConverter.GetBytes(money);var packed=BuildPacked();int delta=packed.Length-source.PackedLength;
        using(var m=new MemoryStream()) {
            m.Write(source.Bytes,0,source.PackedOffset);m.Write(packed,0,packed.Length);
            int tail=source.PackedOffset+source.PackedLength;m.Write(source.Bytes,tail,source.Bytes.Length-tail);
            var result=m.ToArray();
            Buffer.BlockCopy(BitConverter.GetBytes(checked(packed.Length+4)),0,result,source.PackedOffset-9,4);
            Buffer.BlockCopy(BitConverter.GetBytes(packed.Length),0,result,source.PackedOffset-4,4);
            Buffer.BlockCopy(BitConverter.GetBytes(Campaign.Crc(packed,0,packed.Length,0x41916ebdu)),0,result,source.ChecksumOffset+delta,4);
            var check=Campaign.Parse(result);Binary.Require(check.Money==money,"Money verification failed.");return result;
        }
    }
}
