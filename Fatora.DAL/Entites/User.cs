using Fatora.DAL.Entites;
using System.ComponentModel.DataAnnotations;

namespace Fatora.DAL.Entities;


public enum Role
{
    Admin,
    SalesRep
}


public class User
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public required string UserName { get; set; }
    public required string Password { get; set; }
    public required string Name { get; set; }
    public required string  PhoneNumber{ get; set; }
    public string? BusinessName{ get; set; }
    public required string City { get; set; }
    public required string Street { get; set; }
    public Role Role { get; set; }=Role.SalesRep;


    public List<Product> Items { get; set; } = new List<Product>();
    public List<Order> Orders { get; set; } = new List<Order>();
    public List<Customer> Customers { get; set; } = new List<Customer>();

}