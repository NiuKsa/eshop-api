﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using eshopAPI.DataAccess;
using eshopAPI.Helpers;
using eshopAPI.Models;
using eshopAPI.Requests;
using eshopAPI.Requests.Account;
using eshopAPI.Services;
using eshopAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eshop_webAPI.Controllers
{
    [Authorize]
    [Route("api/account")]
    [AutoValidateAntiforgeryToken]
    public class AccountController : Controller
    {
        private readonly IShopUserRepository _shopUserRepository;
        private readonly UserManager<ShopUser> _userManager;
        private readonly SignInManager<ShopUser> _signInManager;
        private readonly ILogger<AccountController> _logger;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;

        // Add required services and they will be injected
        public AccountController(
            IShopUserRepository shopUserRepository,
            UserManager<ShopUser> userManager,
            SignInManager<ShopUser> signInManager,
            IEmailSender emailSender,
            ILogger<AccountController> logger,
            IConfiguration configuration)
        {
            _shopUserRepository = shopUserRepository;
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Register([FromBody]RegisterRequest request)
        {
            var user = new ShopUser { UserName = request.Username, Email = request.Username };
            var result = await _userManager.CreateAsync(user, request.Password);
            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new account with password.");

                await _userManager.AddToRoleAsync(user, UserRole.User.ToString());
                var code = EncodeHelper.Base64Encode(await _userManager.GenerateEmailConfirmationTokenAsync(user));
                var confirmationLink = UrlExtensions.EmailConfirmationLink(user.Id, code, _configuration["RedirectDomain"]);
                await _emailSender.SendConfirmationEmailAsync(request.Username, confirmationLink);
                _logger.LogInformation($"Confirmation email was sent to user: {user.Name}");
                return Ok();
            }

            return BadRequest(result.Errors.Select(e => e.Description));
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Login([FromBody]LoginRequest loginRequest)
        {
            _logger.LogInformation("Call to login from " + loginRequest.Email);

            var result = await _signInManager.PasswordSignInAsync(loginRequest.Email, loginRequest.Password,
                loginRequest.RememberMe, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");
                return Ok();
            }

            return BadRequest("Can not log in");
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return Ok();
        }

        [HttpGet("confirmaccount")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ConfirmEmail(ConfirmUserRequest request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            if (user == null)
            {
                _logger.LogInformation($"User with id: {request.UserId} was not found.");
                return NotFound("User was not found");
            }
            var result = await _userManager.ConfirmEmailAsync(user, EncodeHelper.Base64Decode(request.Code));
            if (result.Succeeded)
                return Ok("User is confirmed");

            return BadRequest();
        }

        [HttpPost("forgotPassword")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var shopUser = await _userManager.FindByEmailAsync(request.Email);

            if (shopUser != null)
            {
                var resetPasswordToken = EncodeHelper.Base64Encode(await _userManager.GeneratePasswordResetTokenAsync(shopUser));
                var resetLink = UrlExtensions.ResetPasswordLink(shopUser.Id, resetPasswordToken,
                    _configuration["RedirectDomain"]);
                await _emailSender.SendResetPasswordEmailAsync(request.Email, resetLink);
                return Ok("Password recovery confirmation link was sent to your e-mail.");
            }
            return NotFound("User with this email was not found");
        }

        // TODO : test this when UI is ready for reset password
        [HttpPost("resetpassword")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ResetPassword([FromBody]ResetPasswordRequest request)
        {
            var shopUser = await _userManager.FindByIdAsync(request.UserId);
            if (shopUser != null)
            {
                var decodedToken = EncodeHelper.Base64Decode(request.Token);
                var resetPasswordResult = await _userManager.ResetPasswordAsync(shopUser, decodedToken, request.Password);
                if (resetPasswordResult.Succeeded)
                {
                    return Ok("Password was reset");
                }

                return BadRequest("Failed to reset password");
            }

            return NotFound("User was not found");
        }

        [HttpPost("changerole")]
        public async Task<IActionResult> ChangeRole([FromBody]RoleChangeRequest request)
        {
            _logger.LogInformation($"Changing role of user with email ${request.Email} to ${request.Role}");

            ShopUser user = await _userManager.FindByEmailAsync(request.Email);

            if (user == null)
            {
                _logger.LogInformation($"Role changing failed, no user with such email found");
                return NotFound();
            }

            try
            {
                UserRole role = (UserRole)Enum.Parse(typeof(UserRole), request.Role);
            }
            // happens if role string cannot be parsed
            catch (ArgumentException e)
            {
                _logger.LogInformation($"Role changing failed, bad role provided");
                return BadRequest();
            }

            IList<string> roles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, roles);

            await _userManager.AddToRoleAsync(user, request.Role);

            _logger.LogInformation($"Role succesfully changed");
            return Ok();
        }

        [HttpPut("changePassword")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            _logger.LogInformation($"Attempt to change paswword for user with email ${User.Identity.Name}");
            ShopUser user = await _userManager.FindByEmailAsync(User.Identity.Name);

            if (user == null)
            {
                _logger.LogInformation("User with such email not found");
                return NotFound();
            }

            var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

            if(result.Succeeded)
            {
                _logger.LogInformation("Password changed successfully");
                return Ok();
            }

            _logger.LogInformation("Attempt to change password failed");
            return BadRequest();
        }
    }
}