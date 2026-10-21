program indexbounds(output);
var a: array[1..5] of integer; i: integer;
procedure fill(n: integer);
var i: integer;
begin
   for i := 1 to n do a[i] := i
end;
begin
   fill(5); writeln('ok');
   fill(6)
end.
