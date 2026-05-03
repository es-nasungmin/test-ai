using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
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
            var loginIdInput = request?.LoginId;
            if (string.IsNullOrWhiteSpace(loginIdInput))
            {
                loginIdInput = request?.Username;
            }

            if (string.IsNullOrWhiteSpace(loginIdInput) || string.IsNullOrWhiteSpace(request?.Password))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "로그인 아이디와 비밀번호를 입력해주세요."
                });
            }

            var loginId = loginIdInput.Trim();
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.LoginId == loginId && u.IsActive);

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
                    LoginId = user.LoginId,
                    Username = user.Username,
                    Role = user.Role,
                    IsApproved = user.IsApproved,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    ApprovedAt = user.ApprovedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
        }

        [HttpGet("check-login-id")]
        [HttpGet("check-username")]
        public async Task<IActionResult> CheckLoginId([FromQuery] string loginId)
        {
            if (string.IsNullOrWhiteSpace(loginId))
            {
                return BadRequest(new { success = false, message = "아이디를 입력해주세요." });
            }

            var normalized = loginId.Trim();
            var exists = await _context.Users.AnyAsync(u => u.LoginId.ToLower() == normalized.ToLower());
            return Ok(new
            {
                success = true,
                exists,
                message = exists ? "이미 사용 중인 아이디입니다." : "사용 가능한 아이디입니다."
            });
        }

        [HttpPost("register")]
        public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.LoginId) || string.IsNullOrWhiteSpace(request.Username)
                || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "로그인 아이디, 사용자명, 비밀번호는 필수입니다."
                });
            }

            var loginId = request.LoginId.Trim();
            var username = request.Username.Trim();

            if (request.Password != request.ConfirmPassword)
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "비밀번호가 일치하지 않습니다."
                });
            }

            if (await _context.Users.AnyAsync(u => u.LoginId.ToLower() == loginId.ToLower()))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "이미 존재하는 로그인 아이디입니다."
                });
            }

            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "이미 존재하는 사용자명입니다."
                });
            }

            var user = new User
            {
                LoginId = loginId,
                Username = username,
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
                    LoginId = user.LoginId,
                    Username = user.Username,
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

        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserDto>> GetMe()
        {
            var userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "인증 정보가 유효하지 않습니다."
                });
            }

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

            if (user == null)
            {
                return NotFound(new LoginResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                });
            }

            return Ok(new UserDto
            {
                Id = user.Id,
                LoginId = user.LoginId,
                Username = user.Username,
                Role = user.Role,
                IsApproved = user.IsApproved,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                ApprovedAt = user.ApprovedAt,
                LastLoginAt = user.LastLoginAt
            });
        }

        [Authorize]
        [HttpPut("me/profile")]
        public async Task<ActionResult<LoginResponse>> UpdateMyProfile([FromBody] UpdateMyProfileRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Username))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "사용자명을 입력해주세요."
                });
            }

            var userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "인증 정보가 유효하지 않습니다."
                });
            }

            var username = request.Username.Trim();
            if (await _context.Users.AnyAsync(u => u.Id != userId && u.Username.ToLower() == username.ToLower()))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "이미 사용 중인 사용자명입니다."
                });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
            if (user == null)
            {
                return NotFound(new LoginResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                });
            }

            user.Username = username;
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                Message = "사용자명이 변경되었습니다.",
                User = new UserDto
                {
                    Id = user.Id,
                    LoginId = user.LoginId,
                    Username = user.Username,
                    Role = user.Role,
                    IsApproved = user.IsApproved,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    ApprovedAt = user.ApprovedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
        }

        [Authorize]
        [HttpPut("me/password")]
        public async Task<ActionResult<LoginResponse>> ChangeMyPassword([FromBody] ChangePasswordRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.CurrentPassword)
                || string.IsNullOrWhiteSpace(request.NewPassword)
                || string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "현재 비밀번호와 새 비밀번호를 입력해주세요."
                });
            }

            if (request.NewPassword != request.ConfirmPassword)
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "새 비밀번호와 확인 비밀번호가 일치하지 않습니다."
                });
            }

            var userId = GetCurrentUserId();
            if (userId <= 0)
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "인증 정보가 유효하지 않습니다."
                });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
            if (user == null)
            {
                return NotFound(new LoginResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                });
            }

            if (!VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "현재 비밀번호가 올바르지 않습니다."
                });
            }

            user.PasswordHash = HashPassword(request.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                Message = "비밀번호가 변경되었습니다."
            });
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
                    LoginId = u.LoginId,
                    Username = u.Username,
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
                    LoginId = u.LoginId,
                    Username = u.Username,
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
        [HttpPut("users/{userId}")]
        public async Task<ActionResult<LoginResponse>> UpdateUser(int userId, [FromBody] UpdateUserByAdminRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.LoginId)
                || string.IsNullOrWhiteSpace(request.Username)
                || string.IsNullOrWhiteSpace(request.Role))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "로그인 아이디, 사용자명, 권한은 필수입니다."
                });
            }

            var loginId = request.LoginId.Trim();
            var username = request.Username.Trim();
            var role = request.Role.Trim().ToLowerInvariant();

            if (role != "admin" && role != "user")
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "권한은 admin 또는 user만 가능합니다."
                });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound(new LoginResponse
                {
                    Success = false,
                    Message = "사용자를 찾을 수 없습니다."
                });
            }

            if (await _context.Users.AnyAsync(u => u.Id != userId && u.LoginId.ToLower() == loginId.ToLower()))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "이미 사용 중인 로그인 아이디입니다."
                });
            }

            if (await _context.Users.AnyAsync(u => u.Id != userId && u.Username.ToLower() == username.ToLower()))
            {
                return BadRequest(new LoginResponse
                {
                    Success = false,
                    Message = "이미 사용 중인 사용자명입니다."
                });
            }

            user.LoginId = loginId;
            user.Username = username;
            user.Role = role;
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                Message = "사용자 정보가 수정되었습니다.",
                User = new UserDto
                {
                    Id = user.Id,
                    LoginId = user.LoginId,
                    Username = user.Username,
                    Role = user.Role,
                    IsApproved = user.IsApproved,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    ApprovedAt = user.ApprovedAt,
                    LastLoginAt = user.LastLoginAt
                }
            });
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
                new System.Security.Claims.Claim("loginId", user.LoginId),
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

        private int GetCurrentUserId()
        {
            var userIdRaw = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
            return int.TryParse(userIdRaw, out var userId) ? userId : 0;
        }

        private bool VerifyPassword(string password, string hash)
        {
            var hashOfInput = HashPassword(password);
            return hashOfInput == hash;
        }

        public class UpdateMyProfileRequest
        {
            public string Username { get; set; } = string.Empty;
        }

        public class ChangePasswordRequest
        {
            public string CurrentPassword { get; set; } = string.Empty;
            public string NewPassword { get; set; } = string.Empty;
            public string ConfirmPassword { get; set; } = string.Empty;
        }

        public class UpdateUserByAdminRequest
        {
            public string LoginId { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
        }
    }
}
