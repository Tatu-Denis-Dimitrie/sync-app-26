namespace SyncApp26.Domain.Entities
{
    public class DepartmentFunction
    {
        public Guid DepartmentId { get; set; }
        public Guid FunctionId { get; set; }

        // Navigation properties
        public Department Department { get; set; }
        public Function Function { get; set; }
    }
}