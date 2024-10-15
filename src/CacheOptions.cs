using Microsoft.Extensions.Caching.Distributed;
using System;

public static class CacheOptions
{
    // Milliseconds
    public static DistributedCacheEntryOptions TenMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(10) };
    public static DistributedCacheEntryOptions TwentyMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(20) };
    public static DistributedCacheEntryOptions FiftyMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50) };
    public static DistributedCacheEntryOptions OneHundredMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(100) };
    public static DistributedCacheEntryOptions TwoHundredMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(200) };
    public static DistributedCacheEntryOptions FiveHundredMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(500) };
    public static DistributedCacheEntryOptions NineHundredMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(900) };

    // Seconds
    public static DistributedCacheEntryOptions OneSecond { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1) };
    public static DistributedCacheEntryOptions TwoSeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2) };
    public static DistributedCacheEntryOptions FiveSeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5) };
    public static DistributedCacheEntryOptions TenSeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10) };
    public static DistributedCacheEntryOptions FifteenSeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15) };
    public static DistributedCacheEntryOptions TwentySeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20) };
    public static DistributedCacheEntryOptions ThirtySeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) };
    public static DistributedCacheEntryOptions FortyFiveSeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(45) };
    public static DistributedCacheEntryOptions FiftySeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(50) };

    // Minutes
    public static DistributedCacheEntryOptions OneMinute { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };
    public static DistributedCacheEntryOptions TwoMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) };
    public static DistributedCacheEntryOptions ThreeMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3) };
    public static DistributedCacheEntryOptions FiveMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    public static DistributedCacheEntryOptions TenMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) };
    public static DistributedCacheEntryOptions FifteenMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) };
    public static DistributedCacheEntryOptions TwentyMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20) };
    public static DistributedCacheEntryOptions TwentyFiveMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(25) };
    public static DistributedCacheEntryOptions ThirtyMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) };
    public static DistributedCacheEntryOptions FortyMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(40) };
    public static DistributedCacheEntryOptions FortyFiveMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(45) };
    public static DistributedCacheEntryOptions FiftyMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(50) };
    public static DistributedCacheEntryOptions SixtyMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60) };
    public static DistributedCacheEntryOptions NinetyMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(90) };

    // Hours
    public static DistributedCacheEntryOptions OneHour { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) };
    public static DistributedCacheEntryOptions TwoHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2) };
    public static DistributedCacheEntryOptions ThreeHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(3) };
    public static DistributedCacheEntryOptions FourHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4) };
    public static DistributedCacheEntryOptions SixHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) };
    public static DistributedCacheEntryOptions EightHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8) };
    public static DistributedCacheEntryOptions TwelveHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12) };
    public static DistributedCacheEntryOptions EighteenHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(18) };
    public static DistributedCacheEntryOptions TwentyFourHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) };

    // Days
    public static DistributedCacheEntryOptions OneDay { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) };
    public static DistributedCacheEntryOptions TwoDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(2) };
    public static DistributedCacheEntryOptions ThreeDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3) };
    public static DistributedCacheEntryOptions FourDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(4) };
    public static DistributedCacheEntryOptions FiveDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(5) };
    public static DistributedCacheEntryOptions SevenDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) };
    public static DistributedCacheEntryOptions TenDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(10) };
    public static DistributedCacheEntryOptions FourteenDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(14) };
    public static DistributedCacheEntryOptions TwentyOneDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(21) };
    public static DistributedCacheEntryOptions TwentyEightDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(28) };

    // Weeks
    public static DistributedCacheEntryOptions OneWeek { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) };
    public static DistributedCacheEntryOptions TwoWeeks { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(14) };
    public static DistributedCacheEntryOptions ThreeWeeks { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(21) };
    public static DistributedCacheEntryOptions FourWeeks { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(28) };

    // Months (approximated as 30 days per month)
    public static DistributedCacheEntryOptions OneMonth { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) };
    public static DistributedCacheEntryOptions TwoMonths { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(60) };
    public static DistributedCacheEntryOptions ThreeMonths { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(90) };
    public static DistributedCacheEntryOptions SixMonths { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(180) };
    public static DistributedCacheEntryOptions NineMonths { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(270) };

    // Years (approximated as 365 days per year)
    public static DistributedCacheEntryOptions OneYear { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(365) };
    public static DistributedCacheEntryOptions TwoYears { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(730) };
    public static DistributedCacheEntryOptions ThreeYears { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1095) };
    public static DistributedCacheEntryOptions FiveYears { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1825) };
    public static DistributedCacheEntryOptions TenYears { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(3650) };

    // Infinite (No expiration)
    public static DistributedCacheEntryOptions Infinite { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.MaxValue };

    // Custom durations for specific use cases
    public static DistributedCacheEntryOptions FifteenMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(15) };
    public static DistributedCacheEntryOptions SeventyFiveMilliseconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(75) };
    public static DistributedCacheEntryOptions TwentyFiveSeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(25) };
    public static DistributedCacheEntryOptions FiftyFiveSeconds { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(55) };
    public static DistributedCacheEntryOptions FiftyFiveMinutes { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(55) };
    public static DistributedCacheEntryOptions OnePointFiveHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1.5) };
    public static DistributedCacheEntryOptions FourPointFiveHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4.5) };
    public static DistributedCacheEntryOptions TwentyHours { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(20) };
    public static DistributedCacheEntryOptions FifteenDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(15) };
    public static DistributedCacheEntryOptions ThirtyFiveDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(35) };
    public static DistributedCacheEntryOptions FortyFiveDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(45) };
    public static DistributedCacheEntryOptions SixtyDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(60) };
    public static DistributedCacheEntryOptions OneHundredEightyDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(180) };
    public static DistributedCacheEntryOptions TwoHundredSeventyDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(270) };
    public static DistributedCacheEntryOptions ThreeHundredSixtyFiveDays { get; } = new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(365) };
}

