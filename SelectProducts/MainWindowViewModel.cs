﻿using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;

namespace SelectProducts
{
    public class MainWindowViewModel : ReactiveObject
    {
        #region Declarations
        NorthwindContext db;

        ReadOnlyObservableCollection<ProductInOrder> _productsInOrder;
        ReadOnlyObservableCollection<ProductOnStore> _productsInStore;

        SourceCache<Product, int> products;
        SourceList<ProductInOrder> productsInOrder;
        #endregion

        public MainWindowViewModel()
        {
            db = new NorthwindContext();

            products = new SourceCache<Product, int>(p => p.ProductID);
            productsInOrder = new SourceList<ProductInOrder>();

            products.AddOrUpdate(db.Products);

            this.WhenValueChanged(vm => vm.SelectedProduct).Subscribe(p => AddToOrder(p));
            var canUnSelectExecute = this.WhenAnyValue(x => x.SelectedProductInOrder)
                .Select(selectedProductInOrder => selectedProductInOrder == null ? false : true);

            var productsInStore = products.Connect()
                .Transform(product => new ProductOnStore(product))
                .Filter(x => x.UnitsInStock != 0)
                .Sort(SortExpressionComparer<ProductOnStore>.Ascending(item => item.ProductID))
                .ObserveOnDispatcher()
                .Bind(out _productsInStore)
                .Subscribe();

            IDisposable productsinOrder;
            productsinOrder = productsInOrder.Connect()
                .Sort(SortExpressionComparer<ProductInOrder>.Ascending(x => x.ProductID))
                .Bind(out _productsInOrder)
                .ActOnEveryObject(x => SubscribeToChanges(x), y => SetInitialValues(y));

            UnSelectCommand = ReactiveCommand.Create(RemoveFromOrder, canUnSelectExecute);
        }

        #region Unselect command
        public ReactiveCommand<Unit, Unit> UnSelectCommand { get; }

        private void RemoveFromOrder() => productsInOrder.Remove(SelectedProductInOrder);
        #endregion

        #region Utilities
        private void AddToOrder(ProductOnStore product)
        {
            if (product == null) return;
            if (productsInOrder.Items.Any(o => o.ProductID == product.ProductID)) return;

            ProductInOrder productInOrder = new ProductInOrder(product);
            productsInOrder.Add(productInOrder);
        }

        private void SetInitialValues(ProductInOrder productToRemove)
        {
            productToRemove.SelectedQuantity = 0;
            productToRemove.SourceProductOnStore.UnitsInStock += productToRemove.SourceProductOnStore.UnitsOnOrder;
            productToRemove.SourceProductOnStore.UnitsOnOrder = 0;
            TotalSum -= productToRemove.Sum;
        }

        private void SubscribeToChanges(ProductInOrder newProductInOrder)
        {
            newProductInOrder.WhenAnyValue(x => x.SelectedDiscount, x => x.SelectedQuantity)
            .Subscribe(a =>
            {
                int newSelectedDiscount = a.Item1;
                int newSelectedQuantity = a.Item2;

                //-UnitPrice или +UnitPrice к TotalSum и цене товара в заказке
                decimal newValue = (newSelectedQuantity - (short)newProductInOrder.SourceProductOnStore.UnitsOnOrder) * (decimal)newProductInOrder.UnitPrice;

                //-1% или +1% скидки от товара
                decimal percentageOff = (decimal)(newSelectedDiscount - newProductInOrder.PreviousSelectedDiscount) / 100;

                //Запускается когда меняется SelectedDiscount
                if (percentageOff != 0)
                {
                    newProductInOrder.Sum -= newProductInOrder.SelectedQuantity * (decimal)newProductInOrder.UnitPrice * percentageOff;
                    TotalSum -= newProductInOrder.SelectedQuantity * (decimal)newProductInOrder.UnitPrice * percentageOff;
                }
                //Запускается когда меняется SelectedQuantity и при этом SelectedDiscount больше нуля
                else if (newSelectedDiscount != 0)
                {
                    //
                    decimal sumOff = ((decimal)newProductInOrder.PreviousSelectedDiscount / 100) * newProductInOrder.SelectedQuantity * (decimal)newProductInOrder.UnitPrice;
                    decimal sumToAdd = sumOff - newProductInOrder.Sum;

                    newProductInOrder.Sum = sumOff;
                    TotalSum += sumToAdd;

                    return;
                }

                newProductInOrder.PreviousSelectedDiscount = newSelectedDiscount;

                newProductInOrder.Sum += newValue;
                TotalSum += newValue;
            });

            newProductInOrder.WhenAnyValue(x => x.SelectedQuantity)
                .Select(newSelectedQuantity =>
                {
                    short? unitsOnOrder = newProductInOrder.SourceProductOnStore.UnitsOnOrder;

                    return newSelectedQuantity - unitsOnOrder;
                })
                .Subscribe(newValue =>
                {
                    newProductInOrder.SourceProductOnStore.UnitsInStock -= (short)newValue;
                    newProductInOrder.SourceProductOnStore.UnitsOnOrder += (short)newValue;

                    SaveChangesToProducts(newProductInOrder, (int)newValue);
                });
        }

        /// <summary>
        /// Возвращает новое значение для сумм
        /// </summary>
        /// <param name="newSelectedDiscount"></param>
        /// <param name="previousSelectedDiscount"></param>
        /// <param name="newSelectedQuantity"></param>
        /// <param name="unitsOnOrder"></param>
        /// <param name="unitPrice"></param>
        /// <returns></returns>
        private decimal GetNewValue(int newSelectedQuantity, short? unitsOnOrder, decimal? unitPrice)
        {
            return 0;
        }

        private void SaveChangesToProducts(ProductInOrder newProductInOrder, int newValue)
        {
            Product productToReplace = db.Products.First(x => x.ProductID == newProductInOrder.ProductID);

            productToReplace.UnitsInStock -= (short)newValue;

            db.SaveChanges();
        }
        #endregion

        #region Properties
        public ReadOnlyObservableCollection<ProductInOrder> ProductsInOrder => _productsInOrder;
        public ReadOnlyObservableCollection<ProductOnStore> ProductsInStore => _productsInStore;

        [Reactive] public ProductOnStore SelectedProduct { private get; set; }
        [Reactive] public ProductInOrder SelectedProductInOrder { private get; set; }

        decimal _totalsum;
        public decimal TotalSum
        {
            set { this.RaiseAndSetIfChanged(ref _totalsum, value); }
            get { return _totalsum; }
        }
        #endregion

        #region Screen objects
        public class ProductOnStore : AbstractNotifyPropertyChanged
        {
            public ProductOnStore(Product sourceProduct)
            {
                this.ProductID = sourceProduct.ProductID;
                this.ProductName = sourceProduct.ProductName;
                this.UnitPrice = sourceProduct.UnitPrice;
                this.UnitsInStock = sourceProduct.UnitsInStock;
                this.UnitsOnOrder = sourceProduct.UnitsOnOrder;
            }

            #region Properties
            public int ProductID { set; get; }

            public string ProductName { set; get; }

            public decimal? UnitPrice { set; get; }

            short? _unitsInStock;
            public short? UnitsInStock
            {
                set { SetAndRaise(ref _unitsInStock, value); }
                get { return _unitsInStock; }
            }

            short? _unitsOnOrder;
            public short? UnitsOnOrder
            {
                set { SetAndRaise(ref _unitsOnOrder, value); }
                get { return _unitsOnOrder; }
            }
            #endregion
        }

        public class ProductInOrder : ReactiveObject
        {
            public ProductInOrder(ProductOnStore product)
            {
                ProductID = product.ProductID;
                ProductName = product.ProductName;
                UnitPrice = product.UnitPrice;
                SelectedDiscount = 0;
                SelectedQuantity = 1;

                Quantity = new List<int>();
                Quantity.AddRange(Enumerable.Range(1, Convert.ToInt32(product.UnitsInStock)));
                Discount = new List<int>(100);
                Discount.AddRange(Enumerable.Range(0, 101));

                SourceProductOnStore = product;
            }

            #region Properties
            public int ProductID { set; get; }

            public string ProductName { set; get; }

            public decimal? UnitPrice { set; get; }

            public List<int> Quantity { get; }

            public List<int> Discount { get; }

            [Reactive]
            public int SelectedQuantity { set; get; }

            [Reactive]
            public int SelectedDiscount { set; get; }

            public decimal Sum { set; get; }

            public ProductOnStore SourceProductOnStore { private set; get; }

            public int PreviousSelectedDiscount { set; get; }
            #endregion
        }
        #endregion
    }
}
