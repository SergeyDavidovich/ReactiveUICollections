using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SelectProducts
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        NorthwindContext db;

        public App()
        {
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("MTM1NTk2QDMxMzcyZTMyMmUzMENRTlZYQ1MweEVSNk02VXpQWXZKaTZ2b1A5UUorZmlkSUw4SXV6NmJSVzQ9");
            this.Exit += App_Exit;

            db = new NorthwindContext();
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            foreach (var product in db.Products)
                product.UnitsOnOrder = 0;

            db.SaveChanges();
        }
    }
}
