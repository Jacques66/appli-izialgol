program noresult(output);
function f(n: integer): integer;
begin
   if n > 0 then f := n
end;
begin
   writeln(f(1):2);
   writeln(f(0):2)
end.
