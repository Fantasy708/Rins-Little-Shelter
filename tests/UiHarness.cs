using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

internal static class UiHarness
{
    [STAThread] private static void Main()
    {
        var root = Directory.GetCurrentDirectory(); Directory.CreateDirectory(Path.Combine(root,"docs/verification"));
        double left=SystemParameters.WorkArea.Right-496, top=SystemParameters.WorkArea.Bottom-352;
        var window = new Window { Title="桌宠验收板", Width=480, Height=340, Left=left, Top=top, Background=new SolidColorBrush(Color.FromRgb(245,241,230)), ResizeMode=ResizeMode.NoResize };
        var panel=new Canvas(); window.Content=panel;
        var count=new TextBlock { Text="穿透点击：0", FontSize=18, Foreground=Brushes.DarkSlateGray }; Canvas.SetLeft(count,18); Canvas.SetTop(count,62); panel.Children.Add(count);
        int clicks=0; var probe=new Button { Content="透明角落点击探针", Width=160, Height=32 }; Canvas.SetLeft(probe,8); Canvas.SetTop(probe,8); panel.Children.Add(probe);
        probe.Click+=(_,_)=>{ count.Text="穿透点击："+(++clicks); File.AppendAllText(Path.Combine(root,"docs/verification/ui-events.txt"),DateTime.Now.ToString("O")+" background click "+clicks+Environment.NewLine); };
        var fullscreen=new Button { Content="进入全屏", Width=150, Height=35 }; Canvas.SetLeft(fullscreen,18); Canvas.SetTop(fullscreen,120); panel.Children.Add(fullscreen);
        bool full=false;
        fullscreen.Click+=(_,_)=>{full=!full;window.WindowStyle=full?WindowStyle.None:WindowStyle.SingleBorderWindow;window.Left=full?0:left;window.Top=full?0:top;window.Width=full?SystemParameters.PrimaryScreenWidth:480;window.Height=full?SystemParameters.PrimaryScreenHeight:340;fullscreen.Content=full?"恢复窗口":"进入全屏";File.AppendAllText(Path.Combine(root,"docs/verification/ui-events.txt"),DateTime.Now.ToString("O")+" fullscreen="+full+Environment.NewLine);};
        var label=new TextBlock{Text="仅用于桌宠点击穿透与全屏验收。",FontSize=12,Foreground=Brushes.Gray};Canvas.SetLeft(label,18);Canvas.SetTop(label,175);panel.Children.Add(label);
        new Application().Run(window);
    }
}
