namespace SyncApp26.Domain.Entities
{
    public class Function
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }

        // Navigation properties
        public ICollection<User> Users { get; set; } = new List<User>();
        public ICollection<DepartmentFunction> DepartmentFunctions { get; set; } = new List<DepartmentFunction>();
    }
}