using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Organization
{
    public class FunctionControllerTests
    {
        private readonly Mock<IFunctionService> _functionServiceMock = new();

        private FunctionController CreateController(string role = Roles.Admin)
        {
            var controller = new FunctionController(_functionServiceMock.Object);
            controller.SetUser(Guid.NewGuid(), role: role);
            return controller;
        }

        [Fact]
        public async Task GetAllFunctions_ReturnsOkWithNames()
        {
            var controller = CreateController();
            _functionServiceMock.Setup(s => s.GetAllFunctionNamesAsync()).ReturnsAsync(new[] { "Engineer", "Welder" });

            var result = await controller.GetAllFunctions();

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<string>>(ok.Value);
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task GetFunctionById_ReturnsOk()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();
            _functionServiceMock.Setup(s => s.GetFunctionByIdAsync(id)).ReturnsAsync(new[] { "Engineer" });

            var result = await controller.GetFunctionById(id);

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<string>>(ok.Value);
            Assert.Single(list);
        }

        [Fact]
        public async Task AddFunction_ReturnsOkAndCallsService()
        {
            var controller = CreateController();

            var result = await controller.AddFunction("Electrician");

            Assert.IsType<OkResult>(result);
            _functionServiceMock.Verify(s => s.AddFunctionAsync("Electrician"), Times.Once);
        }

        [Fact]
        public async Task DeleteFunction_ReturnsOkAndCallsService()
        {
            var controller = CreateController();
            var id = Guid.NewGuid();

            var result = await controller.DeleteFunction(id);

            Assert.IsType<OkResult>(result);
            _functionServiceMock.Verify(s => s.DeleteFunctionAsync(id), Times.Once);
        }
    }
}
