using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RadiusXtractUI.Helpers;
using RadiusXtractUI.Models;

namespace RadiusXtractUI.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    public ObservableCollection<HistoryItem> Items { get; } = new(StubData.GetHistoryItems());
    public int RunCount => Items.Count;
}
