{ The eight queens problem by backtracking (N. Wirth, Algorithms + Data
  Structures = Programs, 1976): prints all 92 solutions, one per line, as the
  row of the queen in each column. }
program eightqueens(output);
var i, count: integer;
    a: array[1..8] of boolean;        { no queen in row i }
    b: array[2..16] of boolean;       { no queen on the /-diagonal i+j }
    c: array[-7..7] of boolean;       { no queen on the \-diagonal i-j }
    x: array[1..8] of integer;        { row of the queen in column i }

procedure print;
var k: integer;
begin
   count := count + 1;
   write(count:3, ':');
   for k := 1 to 8 do write(x[k]:2);
   writeln
end;

procedure try(i: integer);
var j: integer;
begin
   for j := 1 to 8 do
      if a[j] and b[i+j] and c[i-j] then
      begin
         x[i] := j;
         a[j] := false; b[i+j] := false; c[i-j] := false;
         if i < 8 then try(i+1) else print;
         a[j] := true; b[i+j] := true; c[i-j] := true
      end
end;

begin
   count := 0;
   for i := 1 to 8 do a[i] := true;
   for i := 2 to 16 do b[i] := true;
   for i := -7 to 7 do c[i] := true;
   try(1);
   writeln(count:1, ' solutions')
end.
