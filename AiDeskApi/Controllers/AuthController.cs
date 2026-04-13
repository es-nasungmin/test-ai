using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using AiDeskApi.Data;
using AiDeskApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AiDeskApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AiDeskContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AiDeskContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Username) || string.IsNullOrWhiteSpace(request?.Password))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "사용자명과 비밀번호를 입력해주세요."
                });
            }

            var username = request.Username.Trim();
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "사용자명 또는 비밀번호가 올바르지 않습니다."
                });
            }

            if (!user.IsApproved)
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "관리자 승인 대기 중인 계정입니다."
                });
            }

            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user);

            return Ok(new LoginResponse
            {
                Success = true,
                Message = "로그인 성공",
                Token = token,
                User = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role,
                    IsApproved = user.IsApproved,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    ApprovedAt = user.ApprovedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
        }

        [HttpPost("register")]
        public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email) 
                || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "사용자명, 이메일, 비밀번호는 필수입니다."
                });
            }

            if (request.Password != request.ConfirmPassword)
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "비밀번호가 일치하지 않습니다."
                });
            }

            // 사용자명 또는 이메일이 이미 존재하는지 확인
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "이미 존재하는 사용자명입니다."
                });
            }

            if (await _context.Users.AnyAsync(u => u.Email == request.Email))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "이미 존재하는 이메일입니다."
                });
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = HashPassword(request.Password),
                Role = "user",
                IsActive = true,
                IsApproved = false // 관리자 승인 필요
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                Message = "회원가입이 완료되었습니다. 관리자의 승인을 기다려주세요.",
                User = new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Role = user.Role,
                    IsApproved = user.IsApproved,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    ApprovedAt = user.ApprovedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
        }

        [HttpPost("validate")]
        public IActionResult Validate()
        {
            // 이 엔드포인트는 JWT 토큰 검증을 위해 사용됨
            // 클라이언트에서 Authorization 헤더에 토큰을 포함하면 자동으로 검증됨
            return Ok(new { valid = true });
        }

        // Admin only endpoints
        [Authorize(Roles = "admin")]
        [HttpGet("users")]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
        {
            var users = await _context.Users
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    Role = u.Role,
                    IsApproved = u.IsApproved,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    ApprovedAt = u.ApprovedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            return Ok(users);
        }

        [Authorize(Roles = "admin")]
        [HttpGet("pending-users")]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetPendingUsers()
        {
            var pendingUsers = await _context.Users
                .Where(u => !u.IsApproved && u.Role == "user")
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    Role = u.Role,
                    IsApproved = u.IsApproved,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    ApprovedAt = u.ApprovedAt,
                    LastLoginAt = u.LastLoginAt
                })
                .ToListAsync();

            return Ok(pendingUsers);
        }

        [Authorize(Roles = "admin")]
        [HttpPost("approve-user/{userId}")]
        public async Task<ActionResult<LoginResponse>> ApproveUser(int userId)
        {
            var userToApprove = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (userToApprove == null)
            {
                return NotFound(new LoginResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                });
            }

            userToApprove.IsApproved = true;
            userToApprove.ApprovedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                Message = $"{userToApprove.Username} 사용자를 승인했습니다."
            });
        }

        [Authorize(Roles = "admin")]
        [HttpPost("reject-user/{userId}")]
        public async Task<ActionResult<LoginResponse>> RejectUser(int userId)
        {
            var userToReject = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (userToReject == null)
            {
                return NotFound(new LoginResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                });
            }

            _context.Users.Remove(userToReject);
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                Message = $"{userToReject.Username} 사용자를 거절했습니다."
            });
        }

        private string GenerateJwtToken(User user)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                jwtSettings["SecretKey"] ?? "your-secret-key-that-is-at-least-32-characters-long"));

            var signingCredentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, user.Username),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, user.Email),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, user.Role)
            };

            var tokenOptions = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(int.Parse(jwtSettings["ExpiryHours"] ?? "24")),
                signingCredentials: signingCredentials
            );

            return new JwtSecurityTokenHandler().WriteToken(tokenOptions);
        }

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private bool VerifyPassword(string password, string hash)
        {
            var hashOfInput = HashPassword(password);
            return hashOfInput == hash;
        }
    }
}
