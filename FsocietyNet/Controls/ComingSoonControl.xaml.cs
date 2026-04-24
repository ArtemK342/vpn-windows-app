using System.Windows.Controls;

namespace FsocietyNet.Controls;

public partial class ComingSoonControl : UserControl
{
    public ComingSoonControl(string title = "")
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(title))
            TitleText.Text = $"// {title.ToUpper()} — В РАЗРАБОТКЕ";
    }
}
