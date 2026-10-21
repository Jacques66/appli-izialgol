program funcresult(output);
{ Revised Report 11: the function block must contain an assignment to the function identifier }
function none(x: integer): integer;
begin
   if x > 0 then writeln('positive')
end;
function nested(x: integer): integer;     { assigned in a nested procedure: accepted }
   procedure setit; begin nested := 2 * x end;
begin setit end;
function later(x: integer): integer; forward;
function later;
begin writeln(x) end;
function recursive(n: integer): integer;  { a call is not an assignment }
begin writeln(recursive(n - 1)) end;
begin
   writeln(nested(1))
end.
