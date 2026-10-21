program recursion(output);
var i: integer;
function ack(m, n: integer): integer;
begin
   if m = 0 then ack := n + 1
   else if n = 0 then ack := ack(m - 1, 1)
   else ack := ack(m - 1, ack(m, n - 1))
end;
function fib(n: integer): integer;
begin if n < 2 then fib := n else fib := fib(n - 1) + fib(n - 2) end;
function depth(n: integer): integer;
begin if n = 0 then depth := 0 else depth := depth(n - 1) + 1 end;
begin
   writeln(ack(2, 3):1, ' ', ack(3, 3):1);
   for i := 0 to 10 do write(fib(i):4); writeln;
   writeln(depth(20000):1)
end.
