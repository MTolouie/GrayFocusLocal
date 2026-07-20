using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Wpf.Helpers
{
    public static class ListBoxBehavior
    {
        public static readonly DependencyProperty AutoScrollToBottomProperty =
            DependencyProperty.RegisterAttached(
                "AutoScrollToBottom",
                typeof(bool),
                typeof(ListBoxBehavior),
                new PropertyMetadata(false, OnAutoScrollToBottomChanged));

        public static bool GetAutoScrollToBottom(DependencyObject obj) => (bool)obj.GetValue(AutoScrollToBottomProperty);
        public static void SetAutoScrollToBottom(DependencyObject obj, bool value) => obj.SetValue(AutoScrollToBottomProperty, value);

        private static void OnAutoScrollToBottomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox listBox)
            {
                // Hook the descriptor to watch for changes to the ItemsSource property itself
                var descriptor = DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListBox));

                if ((bool)e.NewValue)
                {
                    descriptor.AddValueChanged(listBox, OnItemsSourceChanged);
                    HookCollection(listBox); // Hook whatever is currently set
                }
                else
                {
                    descriptor.RemoveValueChanged(listBox, OnItemsSourceChanged);
                    UnhookCollection(listBox);
                }
            }
        }

        private static void OnItemsSourceChanged(object? sender, EventArgs e)
        {
            if (sender is ListBox listBox)
            {
                HookCollection(listBox);
            }
        }

        private static void HookCollection(ListBox listBox)
        {
            UnhookCollection(listBox); // Prevent duplicate subscriptions

            if (listBox.Items.SourceCollection is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += (s, e) => ScrollListBox(listBox, e);
            }
        }

        private static void UnhookCollection(ListBox listBox)
        {
            if (listBox.Items.SourceCollection is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged -= (s, e) => ScrollListBox(listBox, e);
            }
        }

        private static void ScrollListBox(ListBox listBox, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && listBox.Items.Count > 0)
            {
                listBox.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (listBox.Items.Count > 0)
                    {
                        listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                    }
                }));
            }
        }
    }
}