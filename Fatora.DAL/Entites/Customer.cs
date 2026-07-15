using Fatora.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fatora.DAL.Entites;

public class Customer
{
    public int Id{ get; set; }
    public required string Name { get; set; }
    public string? StoreName { get; set; }
    public required string PhoneNumber { get; set; }
    public required string Street { get; set; }
    public required string City { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid UserId { get; set; }
    public User User{ get; set; }
}
