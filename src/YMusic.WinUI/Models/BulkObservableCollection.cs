#nullable enable

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace YMusic.WinUI.Models;

/// <summary>
/// An ObservableCollection that supports batch item replacement with a single
/// NotifyCollectionChangedAction.Reset notification to avoid UI thrashing in virtualized lists.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public void ReplaceAll(IEnumerable<T> items)
    {
        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotification = false;
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnPropertyChanged(e);
        }
    }
}
