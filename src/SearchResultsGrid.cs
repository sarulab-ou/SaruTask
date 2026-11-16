using System.Drawing;
using System.Windows.Forms;

namespace SaruTask
{
    internal sealed class SearchResultsGrid : DataGridView
    {
        internal SearchResultsGrid()
        {
            DoubleBuffered = true;
            ReadOnly = true; MultiSelect = false; RowHeadersVisible = false;
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
    }
}
