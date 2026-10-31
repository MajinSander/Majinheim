// Página "Ajustes de mods" de la edición Server: edita los .cfg de los mods del server.
// Los mods con ServerSync mandan estos valores a todos los jugadores al conectar.
#if SERVER
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Majinheim
{
    public partial class MainForm
    {
        const string Favorites = "★  Los más usados";
        // (archivo, ajuste) que se enseñan en "Los más usados"
        static readonly string[][] FavoriteKeys =
        {
            new[] { "kg.ItemDrawers", "DrawerPickupRange" },
            new[] { "kg.ItemDrawers", "MaxDrawerPickupRange" },
            new[] { "Azumatt.AzuExtendedPlayerInventory", "Extra Inventory Rows" },
            new[] { "Azumatt.AzuExtendedPlayerInventory", "Slots Per Row" },
            new[] { "Azumatt.AzuCraftyBoxes", "Container Range" },
            new[] { "kg.ItemDrawers", "Radius" },
        };

        List<CfgFile> cfgFiles = new List<CfgFile>();
        ListBox cfgList;
        DataGridView cfgGrid;
        TextBox cfgSearch;
        CheckBox cfgOnlyChanged;
        Label cfgDesc, cfgDirty;
        bool cfgFilling;

        string CfgDir { get { return Path.Combine(AppInfo.Profile, @"BepInEx\config"); } }

        Panel BuildConfigPage()
        {
            cfgList = new ListBox { Dock = DockStyle.Fill, BackColor = Th.Card, ForeColor = Th.Text, BorderStyle = BorderStyle.None, Font = Th.F(10), IntegralHeight = false, ItemHeight = 24, DrawMode = DrawMode.OwnerDrawFixed };
            cfgList.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? Th.Input : Th.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                var it = cfgList.Items[e.Index];
                var f = it as CfgFile;
                string text = f != null ? f.Name + (f.Dirty ? "  ●" : "") : it.ToString();
                TextRenderer.DrawText(e.Graphics, text, cfgList.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height), sel ? Th.Accent : (f != null && f.Dirty ? Th.Warn : Th.Text), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            cfgList.SelectedIndexChanged += (s, e) => FillCfgGrid();

            cfgGrid = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Th.Card, BorderStyle = BorderStyle.None, GridColor = Th.Border,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, EditMode = DataGridViewEditMode.EditOnEnter,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 30
            };
            cfgGrid.RowTemplate.Height = 26;
            cfgGrid.DefaultCellStyle.BackColor = Th.Card; cfgGrid.DefaultCellStyle.ForeColor = Th.Text; cfgGrid.DefaultCellStyle.Font = Th.F(9.5f);
            cfgGrid.DefaultCellStyle.SelectionBackColor = Th.Input; cfgGrid.DefaultCellStyle.SelectionForeColor = Th.Text;
            cfgGrid.ColumnHeadersDefaultCellStyle.BackColor = Th.Side; cfgGrid.ColumnHeadersDefaultCellStyle.ForeColor = Th.Muted; cfgGrid.ColumnHeadersDefaultCellStyle.Font = Th.F(9.5f, FontStyle.Bold);
            cfgGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Th.Side;
            cfgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mod", HeaderText = "Mod", ReadOnly = true, Width = 190 });
            cfgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Sec", HeaderText = "Sección", ReadOnly = true, Width = 140 });
            cfgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Ajuste", ReadOnly = true, Width = 210 });
            cfgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Val", HeaderText = "Valor (clic para cambiar)", Width = 170 });
            cfgGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Def", HeaderText = "Por defecto", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            cfgGrid.Columns["Val"].DefaultCellStyle.BackColor = Th.Input;
            cfgGrid.CellValueChanged += (s, e) => { if (!cfgFilling && e.RowIndex >= 0 && e.ColumnIndex == cfgGrid.Columns["Val"].Index) CfgCellChanged(cfgGrid.Rows[e.RowIndex]); };
            cfgGrid.CurrentCellDirtyStateChanged += (s, e) => { if (cfgGrid.IsCurrentCellDirty && cfgGrid.CurrentCell is DataGridViewComboBoxCell) cfgGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            cfgGrid.SelectionChanged += (s, e) => ShowCfgDesc();
            cfgGrid.DataError += (s, e) => { e.ThrowException = false; };

            cfgDesc = Ui.Lbl("", 9.5f, FontStyle.Regular, Th.Muted);
            cfgDesc.AutoSize = false; cfgDesc.Dock = DockStyle.Bottom; cfgDesc.Height = 58; cfgDesc.Padding = new Padding(8, 6, 8, 0);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Th.Card };
            right.Controls.Add(cfgGrid);
            right.Controls.Add(cfgDesc);

            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Th.Card };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.Controls.Add(cfgList, 0, 0);
            split.Controls.Add(right, 1, 0);

            cfgSearch = Ui.Txt("", 260);
            cfgSearch.TextChanged += (s, e) => FillCfgGrid();
            cfgOnlyChanged = Ui.Chk("Solo los que no están por defecto", false);
            cfgOnlyChanged.CheckedChanged += (s, e) => FillCfgGrid();
            cfgDirty = Ui.Lbl("", 10, FontStyle.Bold, Th.Warn);
            var bar = Ui.Row(Ui.Lbl("Buscar"), cfgSearch, cfgOnlyChanged,
                Ui.Btn("GUARDAR CAMBIOS", SaveCfg, true),
                Ui.Btn("Descartar", () => { if (!CfgDirty() || Confirm("¿Descartar los cambios sin guardar?")) ReloadCfg(); }),
                Ui.Btn("Abrir carpeta", () => Ui.Open(CfgDir)),
                cfgDirty);

            ReloadCfg();
            return PageWithTitle("Ajustes de mods", "Los ajustes del server se mandan solos a todos los jugadores (en los mods que lo permiten). Naranja = cambiado y sin guardar; dorado = distinto del valor por defecto. " +
                "Al guardar se hace un backup. En el juego también puedes abrir este menú con F1 (eres admin).", split, bar);
        }

        bool CfgDirty() { return cfgFiles.Any(f => f.Dirty); }

        void ReloadCfg()
        {
            object sel = cfgList.SelectedItem;
            string selName = sel is CfgFile ? ((CfgFile)sel).Path : sel as string;
            cfgFiles = CfgFile.LoadAll(CfgDir);
            cfgList.BeginUpdate();
            cfgList.Items.Clear();
            cfgList.Items.Add(Favorites);
            foreach (var f in cfgFiles) cfgList.Items.Add(f);
            cfgList.EndUpdate();
            int idx = 0;
            for (int i = 0; i < cfgList.Items.Count; i++)
            {
                var f = cfgList.Items[i] as CfgFile;
                if ((f != null && f.Path == selName) || (f == null && (string)cfgList.Items[i] == selName)) idx = i;
            }
            if (cfgList.Items.Count > 0) cfgList.SelectedIndex = idx;
            UpdateCfgDirty();
        }

        IEnumerable<CfgEntry> CfgRows()
        {
            string q = cfgSearch.Text.Trim().ToLowerInvariant();
            IEnumerable<CfgEntry> rows;
            var f = cfgList.SelectedItem as CfgFile;
            if (q != "") rows = cfgFiles.SelectMany(x => x.Entries);   // al buscar, busca en todos los mods
            else if (f != null) rows = f.Entries;
            else rows = FavoriteKeys.Select(k => cfgFiles.Where(x => Path.GetFileNameWithoutExtension(x.Path).Equals(k[0], StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Entries).FirstOrDefault(e => e.Key == k[1])).Where(e => e != null);
            if (q != "") rows = rows.Where(e => (e.Key + " " + e.Section + " " + e.Description + " " + e.File.Name).ToLowerInvariant().Contains(q));
            if (cfgOnlyChanged.Checked) rows = rows.Where(e => !e.IsDefault || e.Changed);
            return rows;
        }

        void FillCfgGrid()
        {
            if (cfgGrid == null) return;
            if (cfgGrid.IsCurrentCellInEditMode) cfgGrid.EndEdit();
            cfgFilling = true;
            cfgGrid.SuspendLayout();
            cfgGrid.Rows.Clear();
            bool many = cfgSearch.Text.Trim() != "" || !(cfgList.SelectedItem is CfgFile);
            cfgGrid.Columns["Mod"].Visible = many;
            foreach (var e in CfgRows().Take(1500))
            {
                int r = cfgGrid.Rows.Add(e.File.Name, e.Section, e.Key, e.Value, e.Default ?? "");
                var row = cfgGrid.Rows[r];
                row.Tag = e;
                if (e.Acceptable.Count > 0 && !e.Multi)
                {
                    var cb = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox };
                    foreach (var a in e.Acceptable) cb.Items.Add(a);
                    if (!e.Acceptable.Contains(e.Value)) cb.Items.Add(e.Value);
                    cb.Value = e.Value;
                    cb.Style.BackColor = Th.Input; cb.Style.ForeColor = Th.Text;
                    row.Cells["Val"] = cb;
                }
                ColorCfgRow(row);
            }
            cfgGrid.ResumeLayout();
            cfgFilling = false;
            ShowCfgDesc();
        }

        void ColorCfgRow(DataGridViewRow row)
        {
            var e = (CfgEntry)row.Tag;
            row.DefaultCellStyle.ForeColor = e.Changed ? Th.Accent : !e.IsDefault && e.Default != null ? Th.Warn : Th.Text;
            row.DefaultCellStyle.Font = e.Changed ? Th.F(9.5f, FontStyle.Bold) : null;
        }

        void CfgCellChanged(DataGridViewRow row)
        {
            var e = (CfgEntry)row.Tag;
            var v = row.Cells["Val"].Value;
            e.Value = v == null ? "" : v.ToString().Trim();
            ColorCfgRow(row);
            string err = e.Validate();
            if (err != null) Status("Ojo: «" + e.Key + "» " + err);
            UpdateCfgDirty();
            cfgList.Invalidate();
        }

        void ShowCfgDesc()
        {
            if (cfgDesc == null) return;
            var e = cfgGrid.CurrentRow != null ? cfgGrid.CurrentRow.Tag as CfgEntry : null;
            if (e == null) { cfgDesc.Text = cfgList.SelectedItem is CfgFile ? "" : "Aquí están los ajustes que más se cambian. Elige un mod a la izquierda para ver todos los suyos, o usa Buscar."; return; }
            string extra = "";
            if (e.Range != null) extra += "  Rango permitido: " + e.Range.Replace("From", "de").Replace(" to ", " a ") + ".";
            if (e.Multi && e.Acceptable.Count > 0) extra += "  Puedes poner varios separados por comas: " + string.Join(", ", e.Acceptable) + ".";
            cfgDesc.Text = e.File.Name + "  ›  " + e.Section + "  ›  " + e.Key + "\n" + (e.Description == "" ? "(sin descripción)" : e.Description) + extra;
        }

        void UpdateCfgDirty()
        {
            int n = cfgFiles.Sum(f => f.Entries.Count(e => e.Changed));
            cfgDirty.Text = n == 0 ? "" : "  ●  " + n + " cambios sin guardar";
        }

        void SaveCfg()
        {
            if (cfgGrid.IsCurrentCellInEditMode) cfgGrid.EndEdit();
            var changed = cfgFiles.SelectMany(f => f.Entries).Where(e => e.Changed).ToList();
            if (changed.Count == 0) { Status("No hay cambios que guardar."); return; }
            var errors = changed.Select(e => new { e, err = e.Validate() }).Where(x => x.err != null).Select(x => "• " + x.e.File.Name + " › " + x.e.Key + ": " + x.err).ToList();
            if (errors.Count > 0) { MessageBox.Show(this, "Hay valores que no valen:\n\n" + string.Join("\n", errors), "Majinheim", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var files = cfgFiles.Where(f => f.Dirty).ToList();
            Backup.Create("ajustes-mods", files.Select(f => new Backup.Item(f.Path, "cfg_" + Path.GetFileNameWithoutExtension(f.Path))), true);
            foreach (var f in files) f.Save();
            foreach (var e in changed) Log.W("Ajuste cambiado: {0} › {1} = {2}", e.File.Name, e.Key, e.Value);
            UpdateCfgDirty();
            FillCfgGrid();
            cfgList.Invalidate();
            string msg = changed.Count + " ajustes guardados (con backup).\n\nLa mayoría de mods aplican el cambio al momento, también a los jugadores conectados. Si alguno no lo nota, reinicia el server.";
            if (runner.Running)
            {
                if (MessageBox.Show(this, msg + "\n\n¿Reiniciar el server ahora para asegurarte? (guarda el mundo antes)", "Majinheim", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    RunBg("Reiniciando el server...", () => { runner.Stop(true); runner.Start(); }, null);
            }
            else MessageBox.Show(this, msg, "Majinheim", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
#endif
