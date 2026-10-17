using System;
using System.Windows.Forms;

namespace SimHandling
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--test", StringComparison.OrdinalIgnoreCase))
            {
                return HandlingTest.Run();
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "NASCAR Heat 5 Sim Handling", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
