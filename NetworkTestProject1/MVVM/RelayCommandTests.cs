using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniMouse.MVVM;
using System;
// testing boilerplate code omni/mvvm/relaycommand.cs
namespace NetworkTestProject1.MVVM
{
    [TestClass]
    public class RelayCommandTests
    {
        [TestMethod]
        public void Constructor_WithNullExecute_ThrowsArgumentNullException()
        {
            // act & Assert
            Assert.ThrowsException<ArgumentNullException>(() => new RelayCommand(null!));
        }

        [TestMethod]
        public void Constructor_WithValidExecute_CreatesCommand()
        {
            // arrange
            Action<object?> execute = _ => { };

            // Act
            var command = new RelayCommand(execute);

            // Assert
            Assert.IsNotNull(command);
        }

        [TestMethod]
        public void Constructor_WithExecuteAndCanExecute_CreatesCommand()
        {
            // Arrange
            Action<object?> execute = _ => { };
            Predicate<object?> canExecute = _ => true;

            // Act
            var command = new RelayCommand(execute, canExecute);

            // Assert
            Assert.IsNotNull(command);
        }

        [TestMethod]
        public void Execute_CallsProvidedAction()
        {
            // Arrange
            bool executed = false;
            var command = new RelayCommand(_ => executed = true);

            // Act
            command.Execute(null);

            // Assert
            Assert.IsTrue(executed);
        }

        [TestMethod]
        public void Execute_PassesParameterToAction()
        {
            // Arrange
            object? receivedParameter = null;
            var command = new RelayCommand(param => receivedParameter = param);
            var expectedParameter = new object();

            // Act
            command.Execute(expectedParameter);

            // Assert
            Assert.AreSame(expectedParameter, receivedParameter);
        }

        [TestMethod]
        public void Execute_WithNullParameter_PassesNull()
        {
            // Arrange
            object? receivedParameter = new object(); // Initialize with non-null
            var command = new RelayCommand(param => receivedParameter = param);

            // Act
            command.Execute(null);

            // Assert
            Assert.IsNull(receivedParameter);
        }

        [TestMethod]
        public void CanExecute_WithNoCanExecutePredicate_ReturnsTrue()
        {
            // Arrange
            var command = new RelayCommand(_ => { });

            // Act
            var result = command.CanExecute(null);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void CanExecute_WithTruePredicate_ReturnsTrue()
        {
            // Arrange
            var command = new RelayCommand(_ => { }, _ => true);

            // Act
            var result = command.CanExecute(null);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void CanExecute_WithFalsePredicate_ReturnsFalse()
        {
            // Arrange
            var command = new RelayCommand(_ => { }, _ => false);

            // Act
            var result = command.CanExecute(null);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CanExecute_PassesParameterToPredicate()
        {
            // Arrange
            object? receivedParameter = null;
            var command = new RelayCommand(
                _ => { },
                param =>
                {
                    receivedParameter = param;
                    return true;
                });
            var expectedParameter = new object();

            // Act
            command.CanExecute(expectedParameter);

            // Assert
            Assert.AreSame(expectedParameter, receivedParameter);
        }

        [TestMethod]
        public void CanExecute_WithConditionalPredicate_ReturnsCorrectValue()
        {
            // Arrange
            var command = new RelayCommand(
                _ => { },
                param => param is int value && value > 0);

            // Act & Assert
            Assert.IsTrue(command.CanExecute(5));
            Assert.IsFalse(command.CanExecute(-1));
            Assert.IsFalse(command.CanExecute(null));
            Assert.IsFalse(command.CanExecute("string"));
        }

        [TestMethod]
        public void CanExecuteChanged_CanAddHandler()
        {
            // Arrange
            var command = new RelayCommand(_ => { });
            EventHandler handler = (s, e) => { };

            // Act - Should not throw
            command.CanExecuteChanged += handler;

            // Assert
            Assert.IsNotNull(command);
        }

        [TestMethod]
        public void CanExecuteChanged_CanRemoveHandler()
        {
            // Arrange
            var command = new RelayCommand(_ => { });
            EventHandler handler = (s, e) => { };
            command.CanExecuteChanged += handler;

            // Act - Should not throw
            command.CanExecuteChanged -= handler;

            // Assert
            Assert.IsNotNull(command);
        }

        [TestMethod]
        public void CanExecuteChanged_AddAndRemoveMultipleHandlers()
        {
            // Arrange
            var command = new RelayCommand(_ => { });
            EventHandler handler1 = (s, e) => { };
            EventHandler handler2 = (s, e) => { };
            EventHandler handler3 = (s, e) => { };

            // Act - Should not throw
            command.CanExecuteChanged += handler1;
            command.CanExecuteChanged += handler2;
            command.CanExecuteChanged += handler3;
            command.CanExecuteChanged -= handler2;
            command.CanExecuteChanged -= handler1;
            command.CanExecuteChanged -= handler3;

            // Assert
            Assert.IsNotNull(command);
        }

        [TestMethod]
        public void Execute_MultipleInvocations_CallsActionEachTime()
        {
            // Arrange
            int executionCount = 0;
            var command = new RelayCommand(_ => executionCount++);

            // Act
            command.Execute(null);
            command.Execute(null);
            command.Execute(null);

            // Assert
            Assert.AreEqual(3, executionCount);
        }

        [TestMethod]
        public void CanExecute_WithStateChangingPredicate_ReflectsCurrentState()
        {
            // Arrange
            bool isEnabled = false;
            var command = new RelayCommand(_ => { }, _ => isEnabled);

            // Act & Assert
            Assert.IsFalse(command.CanExecute(null));

            isEnabled = true;
            Assert.IsTrue(command.CanExecute(null));

            isEnabled = false;
            Assert.IsFalse(command.CanExecute(null));
        }

        [TestMethod]
        public void Execute_WithComplexParameter_HandlesCorrectly()
        {
            // Arrange
            var complexObject = new { Name = "Test", Value = 42 };
            object? receivedParameter = null;
            var command = new RelayCommand(param => receivedParameter = param);

            // Act
            command.Execute(complexObject);

            // Assert
            Assert.AreSame(complexObject, receivedParameter);
        }
    }
}
