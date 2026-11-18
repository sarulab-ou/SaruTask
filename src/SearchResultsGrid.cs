using System;
using System.Drawing;
using System.Windows.Forms;

namespace SaruTask
{
    internal sealed class SearchResultsGrid : DataGridView
    {
        internal Func<bool> CommitRename { get; set; }
        internal Action CancelRename { get; set; }
        internal SearchResultsGrid()
        {
            DoubleBuffered = true;
            EditMode = DataGridViewEditMode.EditProgrammatically;
            MultiSelect = false; RowHeadersVisible = false;
            AllowUserToAddRows = false; AllowUserToDeleteRows = false; AllowUserToResizeRows = false;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            AutoGenerateColumns = false; BackgroundColor = Color.White;
            CellBorderStyle = DataGridViewCellBorderStyle.None;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            BorderStyle = BorderStyle.FixedSingle; EnableHeadersVisualStyles = false;
            ColumnHeadersDefaultCellStyle.BackColor = Color.White;
            ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            DefaultCellStyle.BackColor = Color.White;
            RowTemplate.Height = 26;
        }

        private bool ProcessRenameKey(Keys keyData)
        {
            if (!IsCurrentCellInEditMode) return false;
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Enter) { if (CommitRename == null || CommitRename()) EndEdit(); return true; }
            if (key == Keys.Escape) { if (CancelRename != null) CancelRename(); else { CancelEdit(); EndEdit(); } return true; }
            return false;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            return ProcessRenameKey(keyData) || base.ProcessDialogKey(keyData);
        }

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            return ProcessRenameKey(e.KeyData) || base.ProcessDataGridViewKey(e);
        }
    }
}
