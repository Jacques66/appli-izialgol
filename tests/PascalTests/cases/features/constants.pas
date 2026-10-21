program constants(output);
const n = 10; minus = -n; plus = +n; pi = 3.14159; negpi = -pi;
      letter = 'x'; greeting = 'hello'; big = maxint; nbig = -maxint;
      yes = true;
type small = minus..plus;
var s: small;
begin
   s := minus; writeln(n:3, minus:4, plus:4, s:4);
   writeln(pi:8:5, negpi:9:5);
   writeln(letter, ' ', greeting, ' ', yes);
   writeln(big:1, ' ', nbig:1);
   writeln(big - 1 + 1 = maxint)
end.
