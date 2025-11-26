using OmniMouse.ViewModel;
using System;
using System.Windows;
using System.Windows.Input;

namespace OmniMouse.Views
{
    /// <summary>
    /// Interaction logic for LayoutSelectionView.xaml.
    /// </summary>
    public partial class LayoutSelectionView : Window
    {
        private MachinePositionItem? _draggedItem;
        internal bool IsDialogInstance { get; set; } = false; // added flag

        public LayoutSelectionView()
        {
            // If the XAML-generated InitializeComponent is missing (e.g. XAML not included as Page),
            // provide a minimal stub so the project compiles; enable XAML compilation (Build Action: Page)
            // to get the real generated method.
            InitializeComponent();
        }

        public LayoutSelectionView(LayoutSelectionViewModel viewModel) : this()
        {
            DataContext = viewModel;

            // Subscribe to view model events
            viewModel.LayoutConfirmed += OnLayoutConfirmed;
            viewModel.Cancelled += OnCancelled;

            Closing += (s, e) => viewModel.Cleanup();
        }

        private void OnLayoutConfirmed(object? sender, EventArgs e)
        {
            if (IsDialogInstance)
            {
                try { DialogResult = true; } catch { /* ignore if not modal */ }
            }
            Close();
        }
        
        private void OnCancelled(object? sender, EventArgs e)
        {
            if (IsDialogInstance)
            {
                try { DialogResult = false; } catch { /* ignore if not modal */ }
            }
            Close();
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (_draggedItem != null && !_draggedItem.IsEmptySlot)
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (_draggedItem == null || _draggedItem.IsEmptySlot)
                return;

            // Get the target position from the dropped-on item
            if (sender is FrameworkElement element && element.DataContext is MachinePositionItem targetItem)
            {
                int targetPosition = targetItem.Position;

                // Allow dropping any machine on any position
                if (DataContext is LayoutSelectionViewModel vm)
                {
                    vm.MoveMachine(_draggedItem.MachineId, targetPosition);
                }
            }

            _draggedItem = null;
            e.Handled = true;
        }

        private void OnMachineMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is MachinePositionItem item)
            {
                // Allow dragging any machine (local or peer) except empty slots
                if (!item.IsEmptySlot)
                {
                    _draggedItem = item;
                    DragDrop.DoDragDrop(element, item, DragDropEffects.Move);
                    e.Handled = true;
                }
            }
        }
    }
}
