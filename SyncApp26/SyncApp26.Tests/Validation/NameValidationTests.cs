using System.ComponentModel.DataAnnotations;
using SyncApp26.Shared.DTOs.Request.Department;
using SyncApp26.Shared.DTOs.Request.Organization;
using SyncApp26.Shared.DTOs.Request.User;

namespace SyncApp26.Tests.Validation
{
    public class NameValidationTests
    {
        private static IList<ValidationResult> Validate(object dto)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
            return results;
        }

        [Theory]
        [InlineData("John")]
        [InlineData("Jean-Paul")]
        [InlineData("O'Brien")]
        [InlineData("Ștefan")]
        [InlineData("Maria Elena Ana Cristina Georgiana Andreea Ioana Larisa Teodora Miruna")] // 10 given names
        public void UserRequestDTO_AcceptsValidNames(string name)
        {
            var dto = new UserRequestDTO { FirstName = name, LastName = name, Email = "a@b.com" };

            var results = Validate(dto);

            Assert.Empty(results);
        }

        [Theory]
        [InlineData("John3")]
        [InlineData("John Doe!")]
        [InlineData("O_Brien")]
        [InlineData("")]
        public void UserRequestDTO_RejectsInvalidNames(string name)
        {
            var dto = new UserRequestDTO { FirstName = name, LastName = "Valid", Email = "a@b.com" };

            var results = Validate(dto);

            Assert.NotEmpty(results);
        }

        [Fact]
        public void UserRequestDTO_RejectsNameOverMaxLength()
        {
            var tooLong = new string('a', 101);
            var dto = new UserRequestDTO { FirstName = tooLong, LastName = "Valid", Email = "a@b.com" };

            var results = Validate(dto);

            Assert.NotEmpty(results);
        }

        [Fact]
        public void UserRequestDTO_AcceptsFunctionWithDigitsAndPunctuation()
        {
            var dto = new UserRequestDTO
            {
                FirstName = "John",
                LastName = "Doe",
                Email = "a@b.com",
                Function = "R&D Engineer Level 2"
            };

            var results = Validate(dto);

            Assert.Empty(results);
        }

        [Fact]
        public void UserRequestDTO_RejectsFunctionOverMaxLength()
        {
            var dto = new UserRequestDTO
            {
                FirstName = "John",
                LastName = "Doe",
                Email = "a@b.com",
                Function = new string('a', 101)
            };

            var results = Validate(dto);

            Assert.NotEmpty(results);
        }

        [Fact]
        public void FunctionRequestDTO_RejectsNameOverMaxLength()
        {
            var dto = new FunctionRequestDTO { Name = new string('a', 101) };

            var results = Validate(dto);

            Assert.NotEmpty(results);
        }

        [Fact]
        public void FunctionRequestDTO_AcceptsValidName()
        {
            var dto = new FunctionRequestDTO { Name = "Level 2 Engineer" };

            var results = Validate(dto);

            Assert.Empty(results);
        }

        [Fact]
        public void DepartmentRequestDTO_RejectsNameOverMaxLength()
        {
            var dto = new DepartmentRequestDTO { Name = new string('a', 101) };

            var results = Validate(dto);

            Assert.NotEmpty(results);
        }
    }
}
