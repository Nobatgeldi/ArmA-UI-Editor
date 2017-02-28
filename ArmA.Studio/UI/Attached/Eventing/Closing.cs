﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ArmA.Studio.UI.Attached.Eventing
{
    public class Closing
    {
        public static DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached("Command",
            typeof(ICommand),
            typeof(Closing),
            new UIPropertyMetadata(CommandChanged));

        public static DependencyProperty CommandParameterProperty =
            DependencyProperty.RegisterAttached("CommandParameter",
                                                typeof(object),
                                                typeof(Closing),
                                                new UIPropertyMetadata(null));

        public static void SetCommand(Window target, ICommand value)
        {
            target.SetValue(CommandProperty, value);
        }

        public static void SetCommandParameter(Window target, object value)
        {
            target.SetValue(CommandParameterProperty, value);
        }
        public static object GetCommandParameter(Window target)
        {
            return target.GetValue(CommandParameterProperty);
        }

        private static void CommandChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            var type = target.GetType();
            var ev = type.GetEvent("Closing");
            var method = typeof(Closing).GetMethod("OnClosing");

            if ((e.NewValue != null) && (e.OldValue == null))
            {
                ev.AddEventHandler(target, Delegate.CreateDelegate(ev.EventHandlerType, method));
            }
            else if ((e.NewValue == null) && (e.OldValue != null))
            {
                ev.RemoveEventHandler(target, Delegate.CreateDelegate(ev.EventHandlerType, method));
            }
        }

        public static void OnClosing(object sender, EventArgs e)
        {
            var control = sender as Window;
            var command = (ICommand)control.GetValue(CommandProperty);
            var commandParameter = control.GetValue(CommandParameterProperty);
            command.Execute(commandParameter);
        }
    }

}
