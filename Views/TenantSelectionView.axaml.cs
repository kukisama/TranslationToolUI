using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TrueFluentPro.Models;

namespace TrueFluentPro.Views
{
    public partial class TenantSelectionView : Window
    {
        public TenantSelectionView()
        {
            InitializeComponent();
        }

        public TenantSelectionView(IReadOnlyList<AzureTenantInfo> tenants, string? preselectTenantId = null)
        {
            InitializeComponent();

            var items = (tenants ?? Array.Empty<AzureTenantInfo>()).ToList();
            TenantComboBox.ItemsSource = items;

            var idx = -1;
            if (!string.IsNullOrWhiteSpace(preselectTenantId))
            {
                idx = items.FindIndex(t => string.Equals(t.TenantId, preselectTenantId, StringComparison.OrdinalIgnoreCase));
            }

            TenantComboBox.SelectedIndex = idx >= 0 ? idx : (items.Count > 0 ? 0 : -1);

            OkButton.Click += (_, _) =>
            {
                var selected = TenantComboBox.SelectedItem as AzureTenantInfo;
                Close(selected);
            };

            CancelButton.Click += (_, _) => Close(null);
        }

        public static Task<AzureTenantInfo?> ShowAsync(
            Window owner,
            IReadOnlyList<AzureTenantInfo> tenants,
            string? preselectTenantId = null)
        {
            var view = new TenantSelectionView(tenants, preselectTenantId);
            return view.ShowDialog<AzureTenantInfo?>(owner);
        }
    }
}
