using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FalconBMS.Launcher.Models;

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        if (items is null)
            throw new ArgumentNullException(nameof(items));

        CheckReentrancy();

        // Change the underlying collection directly so WPF is not notified
        // separately for every removed and added item.
        Items.Clear();

        foreach (T item in items)
            Items.Add(item);

        // Notify WPF once after the complete replacement is ready.
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Reset));
    }
}