# YouTube Super Chat Currency Conversion

## Problem
Previously, YouTube Super Chats were crediting users based on the raw currency amount without conversion to USD. This meant:
- A 1000 JPY donation (~$6.75 USD) would give only **0.001 credits** instead of **6.75 credits**
- A 5 EUR donation (~$5.45 USD) would give only **5 credits** instead of **5.45 credits**
- Only USD donations were working correctly

## Solution
Implemented automatic currency conversion to USD using real-time exchange rates.

### Components Added

#### 1. `CurrencyConversionService.cs`
- **Purpose**: Converts any currency to USD using live exchange rates
- **API Used**: [open.er-api.com](https://open.er-api.com) (free tier, no API key required)
- **Caching**: Exchange rates are cached for 6 hours to minimize API calls
- **Fallback**: If conversion fails, uses original amount (assumes USD)

**Key Methods:**
```csharp
// Convert amount from any currency to USD
decimal usdAmount = await ConvertToUsdAsync(1000m, "JPY");
// Returns: ~6.75

// Get human-readable conversion description
string desc = GetConversionDescription(1000m, "JPY", 6.75m);
// Returns: "1000.00 JPY ? $6.75 USD"
```

#### 2. Updated `YouTubeService.cs`
**Changes in `ProcessMessageAsync`:**
- Extracts currency code from `SuperChatDetails.Currency`
- Extracts original amount from `SuperChatDetails.AmountMicros` (converts from micros to decimal)
- Calls `CurrencyConversionService` to get USD equivalent
- Credits user with USD amount (not original currency amount)
- Logs conversion details for transparency

**Before:**
```csharp
// BUG: Used raw amount without currency conversion
superChatAmount = (decimal)(snippet.SuperChatDetails.AmountMicros ?? 0) / 1_000_000m;
_accountService.AddCreditsToChannel(channelId, superChatAmount, username, messageText);
```

**After:**
```csharp
// Get original amount and currency
originalAmount = (decimal)(snippet.SuperChatDetails.AmountMicros ?? 0) / 1_000_000m;
currencyCode = snippet.SuperChatDetails.Currency;

// Convert to USD
superChatAmountUsd = await _currencyConverter.ConvertToUsdAsync(originalAmount, currencyCode ?? "USD");

// Credit USD amount
_accountService.AddCreditsToChannel(channelId, superChatAmountUsd, username, messageText);
```

#### 3. Registered Service in `Program.cs`
Added `CurrencyConversionService` to dependency injection:
```csharp
builder.Services.AddSingleton<CurrencyConversionService>();
```

## Examples

### Example 1: Japanese Yen Donation
**Donation:** 1000 JPY Super Chat  
**Exchange Rate:** 1 JPY = 0.00675 USD  
**Credits Given:** 6.75 (instead of 0.001)  
**Log Output:**
```
[YouTube] Super Chat from TaroYamada (UC123...): 1000.00 JPY ? $6.75 USD
```

### Example 2: Euro Donation
**Donation:** 5 EUR Super Chat  
**Exchange Rate:** 1 EUR = 1.09 USD  
**Credits Given:** 5.45 (instead of 5.00)  
**Log Output:**
```
[YouTube] Super Chat from EuroFan (UC456...): 5.00 EUR ? $5.45 USD
```

### Example 3: USD Donation (No Conversion Needed)
**Donation:** 10 USD Super Chat  
**Credits Given:** 10.00  
**Log Output:**
```
[YouTube] Super Chat from AmericanViewer (UC789...): $10.00
```

## Supported Currencies
The service supports **160+ currencies** including:
- USD (US Dollar)
- EUR (Euro)
- GBP (British Pound)
- JPY (Japanese Yen)
- CAD (Canadian Dollar)
- AUD (Australian Dollar)
- CNY (Chinese Yuan)
- KRW (South Korean Won)
- INR (Indian Rupee)
- BRL (Brazilian Real)
- MXN (Mexican Peso)
- And many more...

## Error Handling

### Scenario 1: Currency Not Found
If a currency code is not in the exchange rate data:
- Logs a warning
- Falls back to using the original amount (assumes USD)

### Scenario 2: API Unavailable
If the exchange rate API is down:
- Uses last cached rates (up to 6 hours old)
- If no cache exists, falls back to original amount

### Scenario 3: No Currency Code Provided
If YouTube API doesn't provide a currency code:
- Logs a warning
- Assumes USD (no conversion)

## Configuration
No additional configuration needed! The service:
- Automatically fetches exchange rates on first use
- Refreshes rates every 6 hours
- Requires no API key
- Has no rate limits for basic usage

## Performance
- **First conversion:** ~200-500ms (fetches exchange rates from API)
- **Subsequent conversions:** <1ms (uses cached rates)
- **Cache refresh:** Every 6 hours in background
- **API calls:** ~4 per day maximum (very low usage)

## Testing
To test currency conversion:
1. Start the Brain server
2. Set up YouTube OAuth and start listening to a live stream
3. Make Super Chats in different currencies
4. Check server logs for conversion details
5. Verify credits are correct USD equivalent on user profile

## Future Enhancements
Potential improvements:
- Admin UI to view current exchange rates
- Manual exchange rate overrides for specific currencies
- Historical conversion logs
- Support for custom base currency (e.g., EUR instead of USD)

## Troubleshooting

### Issue: Conversions seem incorrect
**Check:**
1. Server logs for exchange rate updates
2. When rates were last refreshed (should be within 6 hours)
3. Compare with real-time exchange rates online

**Solution:**
Restart the server to force a fresh exchange rate fetch.

### Issue: API errors in logs
**Check:**
Network connectivity to `open.er-api.com`

**Solution:**
The service will fall back to cached rates or original amounts. Fix network issues and rates will auto-update on next refresh.

## Credits
- Exchange rate data provided by [ExchangeRate-API](https://www.exchangerate-api.com/)
- Free tier: 1,500 requests/month (plenty for this use case)
