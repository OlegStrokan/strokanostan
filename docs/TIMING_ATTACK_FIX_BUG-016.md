# Why BUG-016 mattered: timing attacks on API key comparison

## The problem

Three places compared a secret API key using regular string comparison:

```csharp
if (providedKey != expectedKey)                                  // Order
!string.Equals(provided, expectedKey, StringComparison.Ordinal)  // OpsConsole
!string.Equals(provided, config["AdminApiKey"], ...)              // ProductAdmin
```

Both `!=` and `string.Equals` compare character by character and **return as soon as
they find a mismatch**. That's an optimization for normal code, but it's a problem
for secret comparison.

## Why that leaks information

If the attacker sends a guessed key and measures how long the server takes to
respond, a key that gets the *first* character right takes a few nanoseconds
longer to reject than one that gets it wrong immediately — because the comparison
had to check one more character before bailing out.

By repeating this with many requests and statistics to cancel out network noise,
an attacker can recover the key **one character at a time** instead of having to
guess the whole thing at once. A key that would take forever to brute-force
outright can be solved character-by-character in a much smaller number of attempts.

This is a real, published class of attack (timing side-channel), not theoretical.

## The fix

Replace the comparison with `CryptographicOperations.FixedTimeEquals`:

```csharp
CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(providedKey),
    Encoding.UTF8.GetBytes(expectedKey));
```

This method always inspects **every byte** of both inputs before returning,
regardless of where the first mismatch is. The time it takes no longer depends on
*how much of the guess was correct* — only on the length of the input, which isn't
secret. That closes the side channel.

## Why now, and why these 3 spots

This pattern already existed correctly elsewhere in the codebase (the shipping
webhook signature verifiers), so this fix just made the API-key checks consistent
with that existing, correct pattern instead of introducing something new. The 3
sites fixed were the ones the security review flagged; two more identical copies
(Payment, Inventory) were found while fixing this and logged separately as
BUG-036 for the same treatment.
