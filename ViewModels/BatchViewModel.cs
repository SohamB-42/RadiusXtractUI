using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RadiusXtractUI.Helpers;
using RadiusXtractUI.Models;

namespace RadiusXtractUI.ViewModels;

public partial class BatchViewModel : ObservableObject
{
    public ObservableCollection<BatchFileItem> Files { get; } = new(StubData.GetBatchFiles());

    public int TotalFiles => Files.Count;
    public int CompletedFiles => Files.Count(f => f.Status == "Done");
    public int InProgressFiles => Files.Count(f => f.Status == "Processing");
    public int FailedFiles => Files.Count(f => f.Status == "Error");
}
