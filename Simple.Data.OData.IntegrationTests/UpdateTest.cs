﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Simple.Data.OData.IntegrationTests
{
    using Xunit;

    public class UpdateTest : TestBase
    {
        [Fact]
        public void UpdateSingleField()
        {
            _db.Products.UpdateByProductName(ProductName: "Chai", UnitPrice: 123m);

            var product = _db.Products.FindByProductName("Chai");
            Assert.Equal(123m, product.UnitPrice);
        }

        [Fact]
        public void UpdateWholeRecord()
        {
            var product = _db.Products.FindByProductID(1);
            product.UnitPrice = 123m;

            _db.Products.Update(product);

            product = _db.Products.FindByProductID(1);
            Assert.Equal(123m, product.UnitPrice);
        }

        [Fact]
        public void UpdateSingleAssociation()
        {
            var category = _db.Categories.Insert(CategoryName: "Test1");
            var product = _db.Products.Insert(ProductName: "Test2", UnitPrice: 18m, CategoryID : 1);

            _db.Products.UpdateByProductName(ProductName: "Test2", Category: category);

            product = _db.Products.FindByProductName("Test2");
            Assert.Equal(category.CategoryID, product.CategoryID);
            category = _db.Category.WithProducts().FindByCategoryName("Test1");
            Assert.True(category.Products.Count == 1);
        }

        [Fact]
        public void UpdateMultipleAssociations()
        {
            var category = _db.Categories.Insert(CategoryName: "Test3");
            var product1 = _db.Products.Insert(ProductName: "Test4", UnitPrice: 21m, CategoryID: 1);
            var product2 = _db.Products.Insert(ProductName: "Test5", UnitPrice: 22m, CategoryID: 1);

            _db.Categories.UpdateByCategoryName(CategoryName: "Test3", Products: new object[] { product1, product2 });

            category = _db.Category.WithProducts().FindByCategoryName("Test3");
            Assert.Equal(2, category.Products.Count);
        }
    }
}
