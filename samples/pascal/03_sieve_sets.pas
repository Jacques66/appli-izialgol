{ Sieve of Eratosthenes with sets.
  A Pascal 6000 set holds ordinal values 0..58 (one 60-bit word), so the
  sieve uses an array of sets: the number n is element n mod w of sieve[n div w]. }
program primes(output);
const w = 59;           { elements per set: 0..58 }
      maxw = 16;        { sieve[0..maxw] covers 0..1002 }
      n = 1000;
var sieve, primeset: array[0..maxw] of set of 0..58;
    next, j, k, count: integer;
begin
   for k := 0 to maxw do begin sieve[k] := [0..w-1]; primeset[k] := [] end;
   sieve[0] := sieve[0] - [0, 1];
   next := 2; count := 0;
   while next <= n do
   begin
      if (next mod w) in sieve[next div w] then
      begin
         primeset[next div w] := primeset[next div w] + [next mod w];
         count := count + 1;
         j := next;
         while j <= n do
         begin
            sieve[j div w] := sieve[j div w] - [j mod w];
            j := j + next
         end
      end;
      next := next + 1
   end;
   writeln('There are ', count:1, ' primes below ', n:1, ':');
   k := 0;
   for next := 2 to n do
      if (next mod w) in primeset[next div w] then
      begin
         write(next:5); k := k + 1;
         if k mod 15 = 0 then writeln
      end;
   writeln
end.
