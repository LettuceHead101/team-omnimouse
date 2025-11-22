using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.MVVM;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Collections.ObjectModel;

namespace NetworkTestProject1.MVVM
{

    internal class TestViewModel : ViewModelBase
    {
        private string? _testProperty;
        private int _numericProperty;
        private object? _objectProperty;

        public string? TestProperty
        {
            get => _testProperty;
            set => SetProperty(ref _testProperty, value);
        }

        public int NumericProperty
        {
            get => _numericProperty;
            set => SetProperty(ref _numericProperty, value);
        }

        public object? ObjectProperty
        {
            get => _objectProperty;
            set => SetProperty(ref _objectProperty, value);
        }

        // property with action callback
        private string? _propertyWithAction;
        public int ActionCallCount { get; private set; }

        public string? PropertyWithAction
        {
            get => _propertyWithAction;
            set => SetProperty(ref _propertyWithAction, value, () => ActionCallCount++);
        }

        // Expose protected methods for testing
        public void PublicOnPropertyChanged(string? propertyName)
        {
            OnPropertyChanged(propertyName);
        }

        public bool PublicSetProperty<T>(ref T field, T value, string? propertyName = null)
        {
            return SetProperty(ref field, value, propertyName);
        }

        public bool PublicSetPropertyWithAction<T>(ref T field, T value, Action action, string? propertyName = null)
        {
            return SetProperty(ref field, value, action, propertyName);
        }

        // add public wrapper for CreateObservableCollection to expose it for tests
        public ObservableCollection<T> PublicCreateObservableCollection<T>(IEnumerable<T>? collection = null)
        {
            return CreateObservableCollection(collection);
        }
    }

    [TestClass]
    public class ViewModelBaseTests
    {
        [TestMethod]
        public void ViewModelBase_ImplementsINotifyPropertyChanged()
        {
            // Arrange & Act
            var viewModel = new TestViewModel();

            // Assert
            Assert.IsInstanceOfType(viewModel, typeof(INotifyPropertyChanged));
        }

        [TestMethod]
        public void OnPropertyChanged_RaisesPropertyChangedEvent()
        {
            // Arrange
            var viewModel = new TestViewModel();
            string? raisedPropertyName = null;
            viewModel.PropertyChanged += (sender, args) => raisedPropertyName = args.PropertyName;

            // act
            viewModel.PublicOnPropertyChanged("TestProperty");

            // Assert
            Assert.AreEqual("TestProperty", raisedPropertyName);
        }

        [TestMethod]
        public void OnPropertyChanged_WithNullPropertyName_RaisesEventWithNull()
        {
            // arrange
            var viewModel = new TestViewModel();
            string? raisedPropertyName = "NotNull";
            viewModel.PropertyChanged += (sender, args) => raisedPropertyName = args.PropertyName;

            // Act
            viewModel.PublicOnPropertyChanged(null);

            // Assert
            Assert.IsNull(raisedPropertyName);
        }

        [TestMethod]
        public void OnPropertyChanged_WithNoSubscribers_DoesNotThrow()
        {
            // arrange
            var viewModel = new TestViewModel();

            // Act & Assert - Should not throw
            viewModel.PublicOnPropertyChanged("TestProperty");
        }

        [TestMethod]
        public void OnPropertyChanged_MultipleSubscribers_NotifiesAll()
        {
            // Arrange
            var viewModel = new TestViewModel();
            int callCount = 0;
            viewModel.PropertyChanged += (s, e) => callCount++;
            viewModel.PropertyChanged += (s, e) => callCount++;
            viewModel.PropertyChanged += (s, e) => callCount++;

            // Act
            viewModel.PublicOnPropertyChanged("TestProperty");

            // assert
            Assert.AreEqual(3, callCount);
        }

        [TestMethod]
        public void SetProperty_WithDifferentValue_UpdatesFieldAndReturnsTrue()
        {
            // Arrange
            var viewModel = new TestViewModel();
            string? field = "OldValue";

            // act
            bool result = viewModel.PublicSetProperty(ref field, "NewValue", "TestProperty");

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual("NewValue", field);
        }

        [TestMethod]
        public void SetProperty_WithSameValue_DoesNotUpdateAndReturnsFalse()
        {
            // Arrange
            var viewModel = new TestViewModel();
            string field = "SameValue";

            // act
            bool result = viewModel.PublicSetProperty(ref field, "SameValue", "TestProperty");

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual("SameValue", field);
        }

        [TestMethod]
        public void SetProperty_WithDifferentValue_RaisesPropertyChanged()
        {
            // Arrange
            var viewModel = new TestViewModel();
            string? raisedPropertyName = null;
            viewModel.PropertyChanged += (sender, args) => raisedPropertyName = args.PropertyName;

            // Act
            viewModel.TestProperty = "NewValue";

            // Assert
            Assert.AreEqual("TestProperty", raisedPropertyName);
        }

        [TestMethod]
        public void SetProperty_WithSameValue_DoesNotRaisePropertyChanged()
        {
            // Arrange
            var viewModel = new TestViewModel();
            viewModel.TestProperty = "InitialValue";
            int eventCount = 0;
            viewModel.PropertyChanged += (sender, args) => eventCount++;

            // Act
            viewModel.TestProperty = "InitialValue";

            // Assert
            Assert.AreEqual(0, eventCount);
        }

        [TestMethod]
        public void SetProperty_WithNumericType_WorksCorrectly()
        {
            // Arrange
            var viewModel = new TestViewModel();
            string? raisedPropertyName = null;
            viewModel.PropertyChanged += (sender, args) => raisedPropertyName = args.PropertyName;

            // Act
            viewModel.NumericProperty = 42;

            // Assert
            Assert.AreEqual(42, viewModel.NumericProperty);
            Assert.AreEqual("NumericProperty", raisedPropertyName);
        }

        [TestMethod]
        public void SetProperty_WithReferenceType_WorksCorrectly()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var obj = new object();
            string? raisedPropertyName = null;
            viewModel.PropertyChanged += (sender, args) => raisedPropertyName = args.PropertyName;

            // Act
            viewModel.ObjectProperty = obj;

            // Assert
            Assert.AreSame(obj, viewModel.ObjectProperty);
            Assert.AreEqual("ObjectProperty", raisedPropertyName);
        }

        [TestMethod]
        public void SetProperty_WithNullValues_HandlesCorrectly()
        {
            // Arrange
            var viewModel = new TestViewModel();
            viewModel.TestProperty = "NotNull";

            // Act
            viewModel.TestProperty = null;

            // Assert
            Assert.IsNull(viewModel.TestProperty);
        }

        [TestMethod]
        public void SetProperty_FromNullToNull_ReturnsFalse()
        {
            // Arrange
            var viewModel = new TestViewModel();
            viewModel.TestProperty = null;
            int eventCount = 0;
            viewModel.PropertyChanged += (sender, args) => eventCount++;

            // Act
            viewModel.TestProperty = null;

            // Assert
            Assert.AreEqual(0, eventCount);
        }

        [TestMethod]
        public void SetPropertyWithAction_ValueChanged_ExecutesAction()
        {
            // Arrange
            var viewModel = new TestViewModel();
            //viewModel.PropertyWithAction = "Initial";

            // Act
            viewModel.PropertyWithAction = "Changed";

            // Assert
            Assert.AreEqual(1, viewModel.ActionCallCount);
            Assert.AreEqual("Changed", viewModel.PropertyWithAction);
        }

        [TestMethod]
        public void SetPropertyWithAction_ValueNotChanged_DoesNotExecuteAction()
        {
            // Arrange
            var viewModel = new TestViewModel();
            viewModel.PropertyWithAction = "SameValue";
            viewModel.PropertyWithAction = "Different"; // Reset action count
            int initialCount = viewModel.ActionCallCount;

            // Act
            viewModel.PropertyWithAction = "Different";

            // Assert
            Assert.AreEqual(initialCount, viewModel.ActionCallCount);
        }

        [TestMethod]
        public void SetPropertyWithAction_MultipleChanges_ExecutesActionEachTime()
        {
            // Arrange
            var viewModel = new TestViewModel();

            // Act
            viewModel.PropertyWithAction = "First";
            viewModel.PropertyWithAction = "Second";
            viewModel.PropertyWithAction = "Third";

            // Assert
            Assert.AreEqual(3, viewModel.ActionCallCount);
        }

        [TestMethod]
        public void SetPropertyWithAction_ReturnsTrue_WhenValueChanges()
        {
            // Arrange
            var viewModel = new TestViewModel();
            string? field = "OldValue";
            bool actionExecuted = false;

            // Act
            bool result = viewModel.PublicSetPropertyWithAction(
                ref field,
                "NewValue",
                () => actionExecuted = true,
                "TestProperty");

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(actionExecuted);
            Assert.AreEqual("NewValue", field);
        }

        [TestMethod]
        public void SetPropertyWithAction_ReturnsFalse_WhenValueDoesNotChange()
        {
            // Arrange
            var viewModel = new TestViewModel();
            string field = "SameValue";
            bool actionExecuted = false;

            // Act
            bool result = viewModel.PublicSetPropertyWithAction(
                ref field,
                "SameValue",
                () => actionExecuted = true,
                "TestProperty");

            // Assert
            Assert.IsFalse(result);
            Assert.IsFalse(actionExecuted);
        }

        [TestMethod]
        public void CreateObservableCollection_WithNullCollection_ReturnsEmptyCollection()
        {
            // Arrange
            var viewModel = new TestViewModel();

            // Act
            var collection = viewModel.PublicCreateObservableCollection<string>(null);

            // Assert
            Assert.IsNotNull(collection);
            Assert.AreEqual(0, collection.Count);
        }

        [TestMethod]
        public void CreateObservableCollection_WithoutParameters_ReturnsEmptyCollection()
        {
            // Arrange
            var viewModel = new TestViewModel();

            // Act
            var collection = viewModel.PublicCreateObservableCollection<int>();

            // Assert
            Assert.IsNotNull(collection);
            Assert.AreEqual(0, collection.Count);
        }

        [TestMethod]
        public void CreateObservableCollection_WithSourceCollection_CopiesItems()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var sourceList = new List<string> { "Item1", "Item2", "Item3" };

            // Act
            var collection = viewModel.PublicCreateObservableCollection(sourceList);

            // Assert
            Assert.AreEqual(3, collection.Count);
            CollectionAssert.AreEqual(sourceList, collection.ToList());
        }

        [TestMethod]
        public void CreateObservableCollection_WithEmptySource_ReturnsEmptyCollection()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var sourceList = new List<string>();

            // Act
            var collection = viewModel.PublicCreateObservableCollection(sourceList);

            // Assert
            Assert.IsNotNull(collection);
            Assert.AreEqual(0, collection.Count);
        }

        [TestMethod]
        public void CreateObservableCollection_ReturnsObservableCollection_NotSourceReference()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var sourceList = new List<string> { "Item1" };

            // Act
            var collection = viewModel.PublicCreateObservableCollection(sourceList);
            sourceList.Add("Item2");

            // Assert
            Assert.AreEqual(1, collection.Count); // Should not be affected by source changes
        }

        [TestMethod]
        public void CreateObservableCollection_WithComplexType_WorksCorrectly()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var sourceList = new List<TestViewModel>
            {
                new TestViewModel { TestProperty = "A" },
                new TestViewModel { TestProperty = "B" }
            };

            // Act
            var collection = viewModel.PublicCreateObservableCollection(sourceList);

            // Assert
            Assert.AreEqual(2, collection.Count);
            Assert.AreEqual("A", collection[0].TestProperty);
            Assert.AreEqual("B", collection[1].TestProperty);
        }

        [TestMethod]
        public void PropertyChanged_SendsCorrectSender()
        {
            // Arrange
            var viewModel = new TestViewModel();
            object? sender = null;
            viewModel.PropertyChanged += (s, e) => sender = s;

            // Act
            viewModel.TestProperty = "NewValue";

            // Assert
            Assert.AreSame(viewModel, sender);
        }

        [TestMethod]
        public void SetProperty_MultipleProperties_RaisesCorrectPropertyNames()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var raisedProperties = new List<string?>();
            viewModel.PropertyChanged += (s, e) => raisedProperties.Add(e.PropertyName);

            // Act
            viewModel.TestProperty = "Test";
            viewModel.NumericProperty = 42;
            viewModel.ObjectProperty = new object();

            // Assert
            Assert.AreEqual(3, raisedProperties.Count);
            CollectionAssert.Contains(raisedProperties, "TestProperty");
            CollectionAssert.Contains(raisedProperties, "NumericProperty");
            CollectionAssert.Contains(raisedProperties, "ObjectProperty");
        }
    }
}
