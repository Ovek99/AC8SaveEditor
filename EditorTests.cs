using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public static class EditorTests
{
    static void Check(bool condition,string message) { if(!condition)throw new Exception(message); }
    public static int Run(string file) {
        string report=Path.Combine(Environment.CurrentDirectory,"test-results-"+Path.GetFileNameWithoutExtension(file)+".txt");
        try { Test(file);File.WriteAllText(report,"PASS: "+file+Environment.NewLine+"Original checksum and exact no-op round trip; money limits; unlock and relock in every category; DLC and online entries; companion arrays and tracking flag reset; equipped item cleanup; aircraft fallback and preserved statistics; last-aircraft/hangar and conflict protection; relock idempotence and regrant; unknown data retained; invalid checksum rejected; disk write and exact backup; source unchanged. File integrity tests do not establish in-game safety.");return 0; }
        catch(Exception e) { File.WriteAllText(report,e.ToString());return 1; }
    }
    static void Test(string file) {
        var catalogue=Catalogue.Load();byte[] original=File.ReadAllBytes(file);var source=Campaign.Parse(original);var baseline=new UnlockDocument(source,catalogue);
        Check(Binary.Equal(baseline.Export(source.Money),original),"No-op export changed bytes.");
        foreach(var item in catalogue.Items)baseline.Owned(item);
        Check(Binary.Equal(baseline.Export(source.Money),original),"Ownership inspection changed bytes.");
        foreach(ulong value in new ulong[]{0,1,999999999,ulong.MaxValue,source.Money}) {
            var edited=new UnlockDocument(source,catalogue).Export(value);Check(Campaign.Parse(edited).Money==value,"Money mismatch.");
            for(int i=0;i<edited.Length;i++)Check(edited[i]==original[i] || (i>=source.MoneyOffset && i<source.MoneyOffset+8) || (i>=source.ChecksumOffset && i<source.ChecksumOffset+4),"Money edit changed another byte.");
        }
        // Each category also succeeds on its own, independently of bulk grants.
        foreach(var group in catalogue.Items.GroupBy(x=>x.Category)) {
            var item=group.FirstOrDefault(x=>!baseline.Owned(x)) ?? group.First();var document=new UnlockDocument(source,catalogue);document.Grant(item);
            var result=new UnlockDocument(Campaign.Parse(document.Export(source.Money)),catalogue);Check(result.Owned(item),"Single-item failure: "+item.Key);
        }
        // Insert future/unknown data into a valid in-memory save. Bulk editing
        // must keep both opaque properties and IDs outside the known catalogue.
        var seed=new UnlockDocument(source,catalogue);
        var opaque=new SaveTag {Name="FutureProperty",Type=new SaveType {Name="FuturePropertyType"},Prefix=Binary.Join(SaveReader.FString("FutureProperty"),SaveReader.FString("FuturePropertyType"),new byte[4]),Suffix=new byte[1],Data=new byte[]{7,1,9,4,0,33,85}};
        seed.Sections[0].Tags.Add(opaque);seed.Field("UnlockedSkinIdList").Add("4294967294",null);
        var seeded=Campaign.Parse(seed.Export(source.Money));var before=new UnlockDocument(seeded,catalogue);var all=new UnlockDocument(seeded,catalogue);
        foreach(var item in catalogue.Items)all.Grant(item);
        byte[] bytes=all.Export(123456789);var after=new UnlockDocument(Campaign.Parse(bytes),catalogue);
        foreach(var item in catalogue.Items)Check(after.Owned(item),"Bulk unlock failure: "+item.Key);
        Check(after.Field("UnlockedSkinIdList").Contains("4294967294"),"Unknown skin ID lost.");
        Check(Binary.Equal(after.Root("FutureProperty").Build(),opaque.Build()),"Opaque property changed.");
        Check(after.Root("TotalMRP").Data.SequenceEqual(before.Root("TotalMRP").Data),"Lifetime MRP changed.");
        var changed=new HashSet<string>(new[]{"CurrentMRP","UnlockData","AircraftTypeRecords","OwnedAircrafts","NewlyOwnedAircrafts","UnlockedAircraftTreeNodeIDs","NewlyUnlockedAircraftTreeNodeIDs","FeatureFlagMask","MusicPlayer"});
        foreach(var i in catalogue.Items) {if(!string.IsNullOrEmpty(i.Field))changed.Add(i.Field);if(!string.IsNullOrEmpty(i.Mirror))changed.Add(i.Mirror);}
        foreach(var section in before.Sections)foreach(var tag in section.Tags)if(!changed.Contains(tag.Name))Check(Binary.Equal(tag.Build(),after.Root(tag.Name).Build()),"Unrelated property changed: "+tag.Name);
        foreach(var item in catalogue.Items.Where(x=>!string.IsNullOrEmpty(x.Field) && x.Category!="feature")) {
            foreach(var old in before.Field(item.Field).Values())Check(after.Field(item.Field).Contains(old.Key),"Existing ID removed: "+item.Field);
            if(!string.IsNullOrEmpty(item.Mirror))Check(after.Field(item.Mirror).Contains(item.Id),"Companion list missing ID: "+item.Key);
        }
        foreach(var pair in before.Aircraft())Check(after.Aircraft().Any(x=>x.Key==pair.Key && x.Value==pair.Value),"Aircraft copy count changed.");
        foreach(var record in before.AircraftRecords) {
            var copy=after.AircraftRecords.Single(x=>x.Get("PlaneID").Number()==record.Get("PlaneID").Number());
            foreach(var tag in record.Tags)if(tag.Name!="OwnedWeapons" && tag.Name!="NewlyOwnedWeapons")Check(Binary.Equal(tag.Build(),copy.Get(tag.Name).Build()),"Existing aircraft stats/loadouts changed: "+tag.Name);
            foreach(var id in record.Get("OwnedWeapons").Values())Check(copy.Get("OwnedWeapons").Contains(id.Key),"Existing weapon lost.");
        }
        foreach(var record in after.AircraftRecords.Where(x=>!before.AircraftRecords.Any(y=>y.Get("PlaneID").Number()==x.Get("PlaneID").Number()))) {
            foreach(var tag in record.Tags.Where(x=>new[]{"FlightDistance","FlightTimeMilli","SortieCount"}.Contains(x.Name)))Check(tag.Data.All(x=>x==0),"New record inherited statistics.");
            Check(BitConverter.ToInt32(record.Get("PilotData").Data,0)==0,"New record inherited pilot loadouts.");
        }
        var requestedRules=new HashSet<uint>(catalogue.Items.SelectMany(x=>x.Rules));
        foreach(var record in before.UnlockRecords) {
            var copy=after.UnlockRecords.Single(x=>x.Get("ID").Number()==record.Get("ID").Number());uint id=record.Get("ID").Number();
            foreach(var tag in record.Tags)if(tag.Name!="bIsActivated" || !requestedRules.Contains(id))Check(Binary.Equal(tag.Build(),copy.Get(tag.Name).Build()),"Unrelated unlock tracking changed.");
            if(requestedRules.Contains(id))Check((copy.Get("bIsActivated").Suffix[0]&16)!=0,"Rule was not activated.");
        }
        foreach(var item in catalogue.Items)after.Grant(item);
        Check(Binary.Equal(after.Export(123456789),bytes),"Bulk grant is not idempotent.");
        TestRelocks(Campaign.Parse(bytes),catalogue);
        var bad=(byte[])original.Clone();bad[source.MoneyOffset]^=1;bool rejected=false;try {Campaign.Parse(bad);}catch(InvalidDataException) {rejected=true;}Check(rejected,"Bad checksum accepted.");
        string dir=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Ac8UnlockTest-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(dir);
        try {
            string dest=Path.Combine(dir,"Campaign.sav");Campaign.Write(dest,original);string backup=Campaign.Write(dest,bytes);
            Check(Binary.Equal(File.ReadAllBytes(backup),original),"Backup differs.");Check(Binary.Equal(File.ReadAllBytes(dest),bytes),"Disk data differs.");Campaign.Parse(File.ReadAllBytes(dest));
        } finally {
            Check(Path.GetDirectoryName(dir).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase) && Path.GetFileName(dir).StartsWith("Ac8UnlockTest-"),"Unexpected temporary directory.");
            Directory.Delete(dir,true);
        }
        Check(Binary.Equal(File.ReadAllBytes(file),original),"Original save changed.");
    }
    static void TestRelocks(Campaign source,Catalogue catalogue) {
        foreach(var group in catalogue.Items.GroupBy(x=>x.Category)) {
            var item=group.First();var doc=new UnlockDocument(source,catalogue);doc.ApplyChanges(new UnlockItem[0],new[]{item});
            byte[] edited=doc.Export(source.Money);var check=new UnlockDocument(Campaign.Parse(edited),catalogue);
            Check(!check.Owned(item),"Relock failed: "+item.Key);
            if(!string.IsNullOrEmpty(item.Mirror))Check(!check.Field(item.Mirror).Contains(item.Id),"Relock kept new-item flag: "+item.Key);
            foreach(uint rule in item.Rules)Check((check.UnlockRecords.Single(x=>x.Get("ID").Number()==rule).Get("bIsActivated").Suffix[0]&16)==0,"Relock kept tracking activation.");
            if(item.Category=="aircraft") {
                Check(check.AircraftRecords.Any(x=>x.Get("PlaneID").Number()==item.PlaneId),"Relock deleted aircraft history.");
                Check(!catalogue.Items.Any(x=>x.Category=="weapons" && x.PlaneId==item.PlaneId && check.Owned(x)),"Aircraft weapons stayed unlocked.");
            }
            if(item.Category=="parts" || item.Category=="aircraft")Check(!check.Root("UnlockedAircraftTreeNodeIDs").Contains(item.Category=="parts"?item.Id:item.PlaneId.ToString()),"Relock kept tree reference.");
            Check(check.Root("UnlockedSkinIdList").Contains("4294967294"),"Relock lost unknown ID.");
            Check(Binary.Equal(check.Root("FutureProperty").Build(),new UnlockDocument(source,catalogue).Root("FutureProperty").Build()),"Relock changed opaque data.");
            check.ApplyChanges(new UnlockItem[0],new[]{item});Check(Binary.Equal(check.Export(source.Money),edited),"Relock not idempotent: "+item.Key);
            check.ApplyChanges(new[]{item},new UnlockItem[0]);Check(new UnlockDocument(Campaign.Parse(check.Export(source.Money)),catalogue).Owned(item),"Regrant failed: "+item.Key);
        }
        var allPlanes=catalogue.Items.Where(x=>x.Category=="aircraft").ToList();var allHangars=catalogue.Items.Where(x=>x.Category=="hangars").ToList();
        foreach(var list in new[]{allPlanes,allHangars}) {
            var doc=new UnlockDocument(source,catalogue);bool rejected=false;try {doc.ApplyChanges(new UnlockItem[0],list);}catch(InvalidDataException) {rejected=true;}
            Check(rejected,"Last aircraft/hangar relock allowed.");Check(Binary.Equal(doc.Export(source.Money),source.Bytes),"Rejected plan changed data.");
        }
        var weapon=catalogue.Items.First(x=>x.Category=="weapons");var plane=catalogue.Items.Single(x=>x.Category=="aircraft" && x.PlaneId==weapon.PlaneId);
        var conflict=new UnlockDocument(source,catalogue);bool conflictRejected=false;try {conflict.ApplyChanges(new[]{weapon},new[]{plane});}catch(InvalidDataException) {conflictRejected=true;}Check(conflictRejected,"Conflicting weapon/aircraft plan accepted.");Check(Binary.Equal(conflict.Export(source.Money),source.Bytes),"Conflict changed data.");

        var equipped=new UnlockDocument(source,catalogue);uint fallback=equipped.Aircraft().First(x=>x.Value>0).Key;
        var record=equipped.AircraftRecords.First(x=>x.Get("PlaneID").Number()!=fallback && x.Get("PilotData").Structs().Count>0);
        var target=catalogue.Items.Single(x=>x.Category=="aircraft" && x.PlaneId==record.Get("PlaneID").Number());
        var skin=catalogue.Items.First(x=>x.Category=="skin" && x.Aircraft==target.Name);var part=catalogue.Items.First(x=>x.Category=="parts");var emblem=catalogue.Items.First(x=>x.Category=="emblem");var sp=catalogue.Items.First(x=>x.Category=="weapons" && x.PlaneId==target.PlaneId);
        Action<TagList> equip=list=> {
            list.Get("SkinID").Number(uint.Parse(skin.Id));list.Get("EnabledPartIDs").Add(part.Id,null);int slot=0;list.Get("EmblemIDs").ReplaceValues(id=>slot++==0?emblem.Id:id);
            list.Get("SpWeaponIDs").Data=Binary.Join(BitConverter.GetBytes(1),SaveReader.FString(sp.Weapons[0]));
        };
        var pilots=record.Get("PilotData").Structs();equip(pilots[0]);record.Get("PilotData").Structs(pilots);
        var presets=equipped.Root("AircraftSets").Structs();presets[0].Get("PlaneID").Number(target.PlaneId);equip(presets[0]);equipped.Root("AircraftSets").Structs(presets);
        foreach(string name in new[]{"LastSortiedPlaneIDs","LastSortiedPlaneIDsByCampaignMission"}) {
            var tag=equipped.Root(name);var r=new SaveReader(tag.Data);r.Int();int n=r.Int();for(int i=0;i<n;i++) {r.String();Buffer.BlockCopy(BitConverter.GetBytes(target.PlaneId),0,tag.Data,r.Pos,4);r.Int();}
        }
        var last=equipped.Root("LastSortiedAircraftPilotDataByCampaignMission").Structs();foreach(var pilot in last)equip(pilot);equipped.Root("LastSortiedAircraftPilotDataByCampaignMission").Structs(last);
        var prepared=Campaign.Parse(equipped.Export(source.Money));var cleaned=new UnlockDocument(prepared,catalogue);cleaned.ApplyChanges(new UnlockItem[0],new[]{skin,part,emblem,sp});var checkedDoc=new UnlockDocument(Campaign.Parse(cleaned.Export(source.Money)),catalogue);
        Action<TagList> verify=list=> {
            Check(list.Get("SkinID").Number()==uint.MaxValue,"Equipped skin not reset.");Check(!list.Get("EnabledPartIDs").Contains(part.Id),"Equipped part not removed.");Check(!list.Get("EmblemIDs").Contains(emblem.Id),"Equipped emblem not reset.");Check(!list.Get("SpWeaponIDs").Contains(sp.Weapons[0]),"Equipped weapon not reset.");
        };
        verify(checkedDoc.Root("AircraftSets").Structs()[0]);verify(checkedDoc.AircraftRecords.Single(x=>x.Get("PlaneID").Number()==target.PlaneId).Get("PilotData").Structs()[0]);foreach(var pilot in checkedDoc.Root("LastSortiedAircraftPilotDataByCampaignMission").Structs())verify(pilot);
        var removed=new UnlockDocument(prepared,catalogue);removed.ApplyChanges(new UnlockItem[0],new[]{target});var removedCheck=new UnlockDocument(Campaign.Parse(removed.Export(source.Money)),catalogue);
        Check(removedCheck.Root("AircraftSets").Structs()[0].Get("PlaneID").Number()==fallback,"Aircraft preset did not use fallback.");
        foreach(string name in new[]{"LastSortiedPlaneIDs","LastSortiedPlaneIDsByCampaignMission"}) {var r=new SaveReader(removedCheck.Root(name).Data);r.Int();int n=r.Int();for(int i=0;i<n;i++) {r.String();Check(unchecked((uint)r.Int())==fallback,"Sortie map did not use fallback.");}}
        foreach(var stat in record.Tags.Where(x=>new[]{"FlightDistance","FlightTimeMilli","SortieCount","AerialEnemyDestroyedCount"}.Contains(x.Name)))Check(Binary.Equal(stat.Build(),removedCheck.AircraftRecords.Single(x=>x.Get("PlaneID").Number()==target.PlaneId).Get(stat.Name).Build()),"Relock changed aircraft statistics.");

        var bulk=new UnlockDocument(source,catalogue);var keepPlane=allPlanes.First();var keepHangar=allHangars.First();var relocks=catalogue.Items.Where(x=>x.Key!=keepPlane.Key && x.Key!=keepHangar.Key).ToList();bulk.ApplyChanges(new UnlockItem[0],relocks);var bulkCheck=new UnlockDocument(Campaign.Parse(bulk.Export(source.Money)),catalogue);
        foreach(var item in relocks)Check(!bulkCheck.Owned(item),"Bulk relock failed: "+item.Key);Check(bulkCheck.Owned(keepPlane) && bulkCheck.Owned(keepHangar),"Bulk relock removed retained ownership.");Check(bulkCheck.Root("UnlockedSkinIdList").Contains("4294967294"),"Bulk relock removed unknown ID.");
    }
}
