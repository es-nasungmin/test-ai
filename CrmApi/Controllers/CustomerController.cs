using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CrmApi.Data;
using CrmApi.Models;

namespace CrmApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CustomerController : ControllerBase
    {
        private readonly CrmContext _context;

        public CustomerController(CrmContext context)
        {
            _context = context;
        }

        // GET: api/customer
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Customer>>> GetCustomers()
        {
            return await _context.Customers
                .Include(c => c.Interactions)
                .OrderByDescending(c => c.LastContactDate)
                .ToListAsync();
        }

        // GET: api/customer/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Customer>> GetCustomer(int id)
        {
            var customer = await _context.Customers
                .Include(c => c.Interactions)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (customer == null)
            {
                return NotFound();
            }

            return customer;
        }

        // POST: api/customer
        [HttpPost]
        public async Task<ActionResult<Customer>> CreateCustomer(Customer customer)
        {
            customer.CreatedAt = DateTime.UtcNow;
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetCustomer), new { id = customer.Id }, customer);
        }

        // PUT: api/customer/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCustomer(int id, Customer customer)
        {
            if (id != customer.Id)
            {
                return BadRequest();
            }

            var existingCustomer = await _context.Customers.FindAsync(id);
            if (existingCustomer == null)
            {
                return NotFound();
            }

            existingCustomer.Name = customer.Name;
            existingCustomer.PhoneNumber = customer.PhoneNumber;
            existingCustomer.Email = customer.Email;
            existingCustomer.Company = customer.Company;
            existingCustomer.Position = customer.Position;
            existingCustomer.Notes = customer.Notes;
            existingCustomer.Status = customer.Status;
            existingCustomer.LastContactDate = customer.LastContactDate;

            _context.Customers.Update(existingCustomer);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // DELETE: api/customer/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCustomer(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                return NotFound();
            }

            _context.Customers.Remove(customer);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // GET: api/customer/{id}/interactions
        [HttpGet("{id}/interactions")]
        public async Task<ActionResult<IEnumerable<Interaction>>> GetCustomerInteractions(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                return NotFound();
            }

            return await _context.Interactions
                .Where(i => i.CustomerId == id)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();
        }
    }
}
