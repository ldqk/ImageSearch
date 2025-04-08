namespace 以图搜图
{
    public partial class ErrorsDialog : Form
    {
        public ErrorsDialog(string msg)
        {
            InitializeComponent();
            textBox.Text = msg;
        }

        private void ErrorsDialog_Load(object sender, EventArgs e)
        {
        }
    }
}