using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

public sealed class Editor : Form
{
    readonly Catalogue catalogue=Catalogue.Load();
    readonly TextBox path=new TextBox(),search=new TextBox(),details=new TextBox();
    readonly NumericUpDown money=new NumericUpDown();
    readonly ComboBox category=new ComboBox(),tagFilter=new ComboBox();
    readonly DataGridView grid=new DataGridView();
    readonly Label status=new Label();
    readonly Button save=new Button(),saveAs=new Button(),unlockShown=new Button(),relockShown=new Button(),clear=new Button();
    readonly HashSet<string> owned=new HashSet<string>(),pending=new HashSet<string>(),pendingRelocks=new HashSet<string>();
    Campaign campaign;bool updating;
    static readonly Dictionary<string,string> Categories=new Dictionary<string,string> {
        {"All categories",""},{"Skins","skin"},{"Emblems","emblem"},{"Aircraft","aircraft"},{"Special weapons","weapons"},{"Parts","parts"},{"Medals","medal"},{"Assault records","assault"},{"Music","music"},{"Free missions","missions"},{"Training","training"},{"Menu features / Ace difficulty","feature"},{"Hangars","hangars"},{"Hangar situations","situations"}
    };
    public Editor(string initial) {
        Text="AC8 Save Editor";ClientSize=new Size(1180,800);MinimumSize=new Size(960,650);Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(245,247,250);StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(18)};
        foreach(int h in new[]{96,44,42,42})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));Controls.Add(layout);
        var heading=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};heading.RowStyles.Add(new RowStyle(SizeType.Absolute,42));heading.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        heading.Controls.Add(new Label {Text="AC8 Save Editor  •  Money & unlocks",UseMnemonic=false,Font=new Font("Segoe UI",19,FontStyle.Bold),AutoSize=true});
        heading.Controls.Add(new Label {Text="WARNING: DLC unlocks do not work. Loading an edited save may wipe your save data.\nMake a separate manual backup before editing. Do not rely solely on automatic backups.",Dock=DockStyle.Fill,ForeColor=Color.DarkRed,Font=new Font("Segoe UI",10,FontStyle.Bold)});layout.Controls.Add(heading,0,0);
        var filebar=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};filebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<3;i++)filebar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
        path.ReadOnly=true;path.Dock=DockStyle.Fill;filebar.Controls.Add(path);
        var open=Button("Open save…",()=> { if(!ConfirmDirty())return;using(var d=new OpenFileDialog {Filter="Campaign saves (*.sav)|*.sav|All files (*.*)|*.*",FileName="Campaign.sav"})if(d.ShowDialog()==DialogResult.OK)LoadSave(d.FileName); });filebar.Controls.Add(open);
        Configure(save,"Save",()=>Save(false));Configure(saveAs,"Save as…",()=>Save(true));filebar.Controls.Add(save);filebar.Controls.Add(saveAs);save.Enabled=saveAs.Enabled=false;layout.Controls.Add(filebar,0,1);
        var cashbar=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};cashbar.Controls.Add(new Label {Text="Spendable MRP",AutoSize=true,Margin=new Padding(3,7,12,3)});
        money.Maximum=ulong.MaxValue;money.ThousandsSeparator=true;money.Width=240;money.Enabled=false;money.ValueChanged+=(s,e)=>Status();cashbar.Controls.Add(money);
        cashbar.Controls.Add(new Label {Text="Close the game before saving. Replaced files get a .bak backup.",AutoSize=true,Margin=new Padding(18,7,3,3)});layout.Controls.Add(cashbar,0,2);
        var filters=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=6};
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,74));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,235));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,48));filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));
        filters.Controls.Add(new Label {Text="Search",AutoSize=true,Margin=new Padding(3,6,3,3)});search.Dock=DockStyle.Fill;filters.Controls.Add(search);
        filters.Controls.Add(new Label {Text="Category",AutoSize=true,Margin=new Padding(3,6,3,3)});category.DropDownStyle=ComboBoxStyle.DropDownList;category.Dock=DockStyle.Fill;category.Items.AddRange(Categories.Keys.Cast<object>().ToArray());category.SelectedIndex=0;filters.Controls.Add(category);
        filters.Controls.Add(new Label {Text="Tags",AutoSize=true,Margin=new Padding(3,6,3,3)});tagFilter.DropDownStyle=ComboBoxStyle.DropDownList;tagFilter.Dock=DockStyle.Fill;tagFilter.Items.AddRange(new object[]{"All tags","DLC","Online","Shared","Unclassified","Mission-only","Experimental","Untagged"});tagFilter.SelectedIndex=0;filters.Controls.Add(tagFilter);layout.Controls.Add(filters,0,3);
        grid.Dock=DockStyle.Fill;grid.AllowUserToAddRows=grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.BackgroundColor=Color.White;grid.AutoGenerateColumns=false;grid.BorderStyle=BorderStyle.FixedSingle;grid.RowTemplate.Height=27;
        grid.Columns.Add(new DataGridViewCheckBoxColumn {Name="Unlock",HeaderText="Unlock",Width=65});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="State",HeaderText="State",Width=115,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Item",HeaderText="Item",MinimumWidth=180,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Aircraft",HeaderText="Aircraft",Width=170,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Tags",HeaderText="Tags",Width=190,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn {Name="Id",HeaderText="ID",Width=170,ReadOnly=true});
        foreach(DataGridViewColumn col in grid.Columns)col.SortMode=DataGridViewColumnSortMode.NotSortable;
        grid.CurrentCellDirtyStateChanged+=(s,e)=> { if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged+=(s,e)=> {
            if(updating || e.RowIndex<0 || e.ColumnIndex!=0 || campaign==null)return;
            var row=grid.Rows[e.RowIndex];var item=(UnlockItem)row.Tag;
            Stage(item,Convert.ToBoolean(row.Cells[0].Value));row.Cells[1].Value=State(item);Status();
        };
        grid.SelectionChanged+=(s,e)=>ShowDetails();
        layout.Controls.Add(grid,0,4);
        details.Multiline=true;details.ReadOnly=true;details.Dock=DockStyle.Fill;details.ScrollBars=ScrollBars.Vertical;details.BackColor=Color.White;layout.Controls.Add(details,0,5);
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill};Configure(unlockShown,"Unlock shown",()=>StageShown(true));unlockShown.Width=130;
        Configure(relockShown,"Relock shown",()=>StageShown(false));relockShown.Width=130;
        Configure(clear,"Clear pending",()=> { pending.Clear();pendingRelocks.Clear();RefreshGrid(); });clear.Width=130;unlockShown.Enabled=relockShown.Enabled=clear.Enabled=false;actions.Controls.Add(unlockShown);actions.Controls.Add(relockShown);actions.Controls.Add(clear);
        actions.Controls.Add(new Label {Text="Untick to relock. Aircraft relocks also remove their weapons.",AutoSize=true,Margin=new Padding(12,8,3,3)});layout.Controls.Add(actions,0,6);
        status.Dock=DockStyle.Fill;status.AutoEllipsis=true;layout.Controls.Add(status,0,7);
        search.TextChanged+=(s,e)=>RefreshGrid();category.SelectedIndexChanged+=(s,e)=>RefreshGrid();tagFilter.SelectedIndexChanged+=(s,e)=>RefreshGrid();
        var tips=new ToolTip();tips.SetToolTip(tagFilter,"DLC/Online are content tags, not account entitlements. Unclassified means the table does not identify the scope.");
        FormClosing+=(s,e)=> { if(!ConfirmDirty())e.Cancel=true; };
        RefreshGrid();if(initial!=null)Shown+=(s,e)=>LoadSave(initial);
    }
    static Button Button(string name,Action action) { var button=new Button();Configure(button,name,action);return button; }
    static void Configure(Button button,string name,Action action) { button.Text=name;button.Dock=DockStyle.Fill;button.Height=32;button.Click+=(s,e)=>action(); }
    void Stage(UnlockItem item,bool unlocked) {
        pending.Remove(item.Key);pendingRelocks.Remove(item.Key);
        if(unlocked && !owned.Contains(item.Key))pending.Add(item.Key);
        if(!unlocked && owned.Contains(item.Key))pendingRelocks.Add(item.Key);
    }
    string State(UnlockItem item) { return pendingRelocks.Contains(item.Key)?"Relock pending":pending.Contains(item.Key)?"Unlock pending":owned.Contains(item.Key)?"Unlocked":"Locked"; }
    void StageShown(bool unlocked) { if(campaign==null)return;grid.EndEdit();foreach(DataGridViewRow row in grid.Rows)Stage((UnlockItem)row.Tag,unlocked);RefreshGrid(); }
    bool Dirty { get { return campaign!=null && (pending.Count>0 || pendingRelocks.Count>0 || (ulong)money.Value!=campaign.Money); } }
    bool ConfirmDirty() {
        if(!Dirty)return true;
        var answer=MessageBox.Show(this,"Save your pending money/unlock changes?","Unsaved changes",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        return answer==DialogResult.No || (answer==DialogResult.Yes && Save(false));
    }
    void Status() {
        if(campaign==null) {status.Text="Open Campaign.sav. Online tags refer to local campaign entries; separate online ownership is not edited.";return;}
        status.Text=grid.Rows.Count+" shown • "+pending.Count+" unlocks / "+pendingRelocks.Count+" relocks pending. Changes apply only on Save. Earned items may be granted again by the game.";
    }
    void ShowDetails() {
        if(grid.CurrentRow==null) { details.Text="";return; }
        var item=grid.CurrentRow.Tag as UnlockItem;
        if(item!=null)details.Text=item.Name+"  |  "+item.Category+"  |  ID: "+item.Id+"  "+item.Tags+Environment.NewLine+item.Description.Trim();
    }
    void RefreshGrid() {
        if(grid.Columns.Count==0)return;updating=true;grid.SuspendLayout();
        try {
            grid.Rows.Clear();string cat=Categories[(string)category.SelectedItem],tag=(string)tagFilter.SelectedItem,q=search.Text.Trim();
            foreach(var item in catalogue.Items) {
                if(cat!="" && item.Category!=cat)continue;
                if(tag=="Untagged" && item.Tags.Trim()!="")continue;
                if(tag!="All tags" && tag!="Untagged" && item.Tags.IndexOf("["+tag,StringComparison.OrdinalIgnoreCase)<0)continue;
                if(q!="" && (item.Name+" "+item.Aircraft+" "+item.Tags+" "+item.Id+" "+item.Description).IndexOf(q,StringComparison.OrdinalIgnoreCase)<0)continue;
                bool has=owned.Contains(item.Key),staged=pending.Contains(item.Key);
                int index=grid.Rows.Add((has || staged) && !pendingRelocks.Contains(item.Key),State(item),item.Name,item.Aircraft,item.Tags,item.Id);
                var row=grid.Rows[index];row.Tag=item;row.Cells[0].ReadOnly=campaign==null;
                if(has)row.DefaultCellStyle.ForeColor=Color.FromArgb(90,105,95);
            }
        } finally { grid.ResumeLayout();updating=false;ShowDetails();Status(); }
    }
    void LoadSave(string file) {
        try {
            var c=Campaign.Parse(File.ReadAllBytes(file));var doc=new UnlockDocument(c,catalogue);
            var values=new HashSet<string>(catalogue.Items.Where(x=>doc.Owned(x)).Select(x=>x.Key));
            Binary.Require(Binary.Equal(doc.BuildPacked(),c.Bytes.Skip(c.PackedOffset).Take(c.PackedLength).ToArray()),"Ownership read changed save data.");
            campaign=c;owned.Clear();owned.UnionWith(values);pending.Clear();pendingRelocks.Clear();path.Text=Path.GetFullPath(file);money.Value=c.Money;
            money.Enabled=save.Enabled=saveAs.Enabled=unlockShown.Enabled=relockShown.Enabled=clear.Enabled=true;RefreshGrid();
        } catch(Exception e) { MessageBox.Show(this,e.Message,"Cannot open save",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
    bool Save(bool asNew) {
        if(campaign==null)return false;
        try {
            grid.EndEdit();string dest=path.Text;
            if(asNew)using(var d=new SaveFileDialog {Filter="Save game (*.sav)|*.sav",FileName="Campaign.edited.sav",InitialDirectory=Path.GetDirectoryName(dest)}) { if(d.ShowDialog()!=DialogResult.OK)return false;dest=d.FileName; }
            if(string.Equals(Path.GetFullPath(dest),path.Text,StringComparison.OrdinalIgnoreCase) && !Binary.Equal(File.ReadAllBytes(dest),campaign.Bytes))throw new IOException("The save changed since you opened it. Reopen it before saving.");
            Cursor=Cursors.WaitCursor;
            var doc=new UnlockDocument(campaign,catalogue);var selected=catalogue.Items.Where(x=>pending.Contains(x.Key)).ToList();var relocked=catalogue.Items.Where(x=>pendingRelocks.Contains(x.Key)).ToList();doc.ApplyChanges(selected,relocked);
            byte[] bytes=doc.Export((ulong)money.Value);var check=new UnlockDocument(Campaign.Parse(bytes),catalogue);
            foreach(var item in selected)Binary.Require(check.Owned(item),"Unlock verification failed: "+item.Name);
            foreach(var item in relocked)Binary.Require(!check.Owned(item),"Relock verification failed: "+item.Name);
            string backup=Campaign.Write(dest,bytes);LoadSave(dest);
            status.Text=backup==null?"Saved and verified. "+selected.Count+" unlocks / "+relocked.Count+" relocks applied.":"Saved and verified. Backup: "+Path.GetFileName(backup);return true;
        } catch(Exception e) { MessageBox.Show(this,e.Message,"Cannot save",MessageBoxButtons.OK,MessageBoxIcon.Error);return false; }
        finally { Cursor=Cursors.Default; }
    }
    [STAThread] public static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--self-test")return EditorTests.Run(args[1]);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2 && args[0]=="--ui-test") {
            try { using(var editor=new Editor(args[1])) { editor.ShowInTaskbar=false;editor.Opacity=0;editor.Show();Application.DoEvents();editor.Preview();editor.Close(); }return 0; }
            catch(Exception e) { File.WriteAllText("ui-test-results.txt",e.ToString());return 1; }
        }
        try { Application.Run(new Editor(args.Length>0?args[0]:null));return 0; }catch(Exception e) { MessageBox.Show(e.Message,"AC8 Save Editor",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1; }
    }
    void Preview() {
        Binary.Require(campaign!=null,"UI could not load save.");
        category.SelectedItem="Emblems";tagFilter.SelectedItem="Online";Application.DoEvents();
        Binary.Require(grid.Rows.Count==173,"Online tag filter returned incorrect count.");
        var row=grid.Rows.Cast<DataGridViewRow>().First(x=>!owned.Contains(((UnlockItem)x.Tag).Key));row.Cells[0].Value=true;Application.DoEvents();
        Binary.Require(pending.Count==1,"Checkbox did not stage unlock.");
        var existing=grid.Rows.Cast<DataGridViewRow>().First(x=>owned.Contains(((UnlockItem)x.Tag).Key));var existingItem=(UnlockItem)existing.Tag;
        Binary.Require(!existing.Cells[0].ReadOnly,"Existing unlock cannot be relocked.");existing.Cells[0].Value=false;Application.DoEvents();
        Binary.Require(pendingRelocks.Contains(existingItem.Key),"Checkbox did not stage relock.");existing.Cells[0].Value=true;Application.DoEvents();
        Binary.Require(pendingRelocks.Count==0,"Restoring checkbox did not cancel relock.");existing.Cells[0].Value=false;Application.DoEvents();
        using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save("editor-online-preview.png"); }
        category.SelectedItem="Skins";tagFilter.SelectedItem="DLC";Application.DoEvents();
        Binary.Require(grid.Rows.Count>0 && pending.Count==1 && pendingRelocks.Count==1,"Filter lost pending selection.");
        using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save("editor-dlc-preview.png"); }
        StageShown(false);Binary.Require(pending.Count==1,"Bulk relock affected a hidden pending unlock.");StageShown(true);
        clear.PerformClick();Binary.Require(pending.Count==0 && pendingRelocks.Count==0,"Clear pending did not restore original state.");money.Value=campaign.Money;
        File.WriteAllText("ui-test-results.txt","PASS: file load, filters, unlock/relock checkbox staging and cancellation, pending selections across filters, bulk staging, clear pending, warning and window rendering; no save written.");
    }
}
