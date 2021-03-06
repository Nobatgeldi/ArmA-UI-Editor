﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ArmA_UI_Editor.Code.AddInUtil;
using SQF.ClassParser;

namespace ArmA_UI_Editor.UI.Snaps
{
    /// <summary>
    /// Interaction logic for PropertyWindow.xaml
    /// </summary>
    public partial class ToolboxSnap : Page, Code.Interface.ISnapWindow
    {
        public int AllowedCount { get { return 1; } }
        public Dock DefaultDock { get { return Dock.Left; } }

        public ToolboxSnap()
        {
            InitializeComponent();
        }

        public void UnloadSnap()
        {

        }

        public void LoadSnap()
        {

        }
        private void ToolBox_Initialized(object sender, EventArgs e)
        {
            foreach (var addIn in Code.AddInManager.Instance.AddIns)
            {
                foreach (var it in addIn.UIElements)
                {
                    this.ToolBox.Items.Add(it);
                }
            }
        }

        private Point? ToolBoxLastMouseDown;
        private void ToolBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ToolBoxLastMouseDown = e.GetPosition(null);
        }

        private void ToolBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && ToolBoxLastMouseDown.HasValue)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = ToolBoxLastMouseDown.Value - mousePos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    ToolBoxLastMouseDown = null;
                    var data = new Code.UI.DragDrop.UiElementsListBoxData((Code.AddInUtil.UIElement)(sender as ListBox).SelectedItem);
                    DragDrop.DoDragDrop(sender as ListBox, new DataObject("UiElementsListBoxData", data), DragDropEffects.Move);
                    e.Handled = true;
                }
            }
        }
    }
}
