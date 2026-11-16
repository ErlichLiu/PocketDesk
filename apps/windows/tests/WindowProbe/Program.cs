using System.Drawing;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var window = Create("PocketDesk 验收 A", 300);
        Application.Run(window);
    }

    static Form Create(string title, int x)
    {
        var form = new Form { Text = title, StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(x, 300, 760, 620), Font = new Font("Microsoft YaHei UI", 12),
            BackColor = Color.FromArgb(246,248,251), MinimumSize = new Size(300,300) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount=1, RowCount=3, Padding=new Padding(20) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.Controls.Add(new Label { Text="PocketDesk 临时验收窗口\n用于检查铺满、切换、原位还原；不包含用户数据。", AutoSize=true, Dock=DockStyle.Fill },0,0);
        var buttons = new FlowLayoutPanel { Dock=DockStyle.Fill };
        var maximize = new Button { Text="最大化", AutoSize=true, Height=40 };
        maximize.Click += (_,_) => form.WindowState = FormWindowState.Maximized;
        var second = new Button { Text="打开验收 B", AutoSize=true, Height=40 };
        second.Click += (_,_) => Create("PocketDesk 验收 B", 1150).Show();
        buttons.Controls.Add(maximize); buttons.Controls.Add(second);
        layout.Controls.Add(buttons,0,1);
        var text = new TextBox { Multiline=true, ReadOnly=true, Dock=DockStyle.Fill, ScrollBars=ScrollBars.Vertical,
            Text=string.Join(Environment.NewLine,Enumerable.Range(1,35).Select(n=>$"{n:00}  测试内容：窗口列表 / 返回 / 还原到电脑")), BorderStyle=BorderStyle.FixedSingle };
        layout.Controls.Add(text,0,2); form.Controls.Add(layout); return form;
    }
}
