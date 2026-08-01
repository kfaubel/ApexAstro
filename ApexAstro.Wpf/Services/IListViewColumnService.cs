namespace ApexAstro.Wpf.Services
{
    public interface IListViewColumnService
    {
        void UpdateListViewColumns();
        void AutoResizeColumns();
        void UpdateFileColumnWidth();
        void ResetAllColumnWidths();
        void RecalculateColumnWidthsFromData();
        double CalculateTotalColumnsWidth();
        void AdjustSplitterForOptimalWidth();
        void SaveCurrentColumnOrder();
        void UpdateSortIndicators(string? sortColumn, System.ComponentModel.ListSortDirection? sortDirection);
    }
}
