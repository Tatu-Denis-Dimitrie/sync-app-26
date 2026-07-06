using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Organization
{
    public class DepartmentFunctionControllerTests
    {
        private readonly Mock<IDepartmentFunctionService> _departmentFunctionServiceMock = new();

        private DepartmentFunctionController CreateController(string role = "Admin")
        {
            var controller = new DepartmentFunctionController(_departmentFunctionServiceMock.Object);
            controller.SetUser(Guid.NewGuid(), role: role);
            return controller;
        }

        [Fact]
        public async Task GetFunctionsByDepartmentId_ReturnsOkWithFunctionNames()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();
            _departmentFunctionServiceMock.Setup(s => s.GetFunctionsByDepartmentIdAsync(departmentId))
                .ReturnsAsync(new[] { "Engineer", "Manager" });

            var result = await controller.GetFunctionsByDepartmentId(departmentId);

            var ok = Assert.IsType<OkObjectResult>(result);
            var list = Assert.IsAssignableFrom<IEnumerable<string>>(ok.Value);
            Assert.Equal(2, list.Count());
        }

        [Fact]
        public async Task AddFunctionToDepartment_ReturnsNoContentAndCallsService()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();

            var result = await controller.AddFunctionToDepartment(departmentId, "Welder");

            Assert.IsType<NoContentResult>(result);
            _departmentFunctionServiceMock.Verify(s => s.AddFunctionToDepartmentAsync(departmentId, "Welder"), Times.Once);
        }

        [Fact]
        public async Task RemoveFunctionFromDepartment_ReturnsNoContentAndCallsService()
        {
            var controller = CreateController();
            var departmentId = Guid.NewGuid();

            var result = await controller.RemoveFunctionFromDepartment(departmentId, "Welder");

            Assert.IsType<NoContentResult>(result);
            _departmentFunctionServiceMock.Verify(s => s.RemoveFunctionFromDepartmentAsync(departmentId, "Welder"), Times.Once);
        }
    }
}
