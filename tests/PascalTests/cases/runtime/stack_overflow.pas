program stackoverflow(output);
function f(n: integer): integer;
begin f := f(n + 1) + 1 end;
begin writeln(f(0)) end.
