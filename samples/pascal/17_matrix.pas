{ Matrix multiplication with two-dimensional arrays (array[1..n, 1..n] is
  an abbreviation of array[1..n] of array[1..n]); whole-array assignment and
  a matrix power computed by repeated squaring. }
program matrix(output);
const n = 3;
type matrix = array[1..n, 1..n] of integer;
var a, b, c: matrix;
    i, j: integer;

procedure multiply(var x, y, z: matrix);
{ z := x * y }
var i, j, k, s: integer;
    r: matrix;
begin
   for i := 1 to n do
      for j := 1 to n do
      begin
         s := 0;
         for k := 1 to n do s := s + x[i, k] * y[k][j];
         r[i, j] := s
      end;
   z := r
end;

procedure print(var m: matrix);
var i, j: integer;
begin
   for i := 1 to n do
   begin
      for j := 1 to n do write(m[i, j]:8);
      writeln
   end
end;

procedure power(var m: matrix; e: integer; var res: matrix);
var sq: matrix;
    i, j: integer;
begin
   for i := 1 to n do
      for j := 1 to n do
         if i = j then res[i, j] := 1 else res[i, j] := 0;
   sq := m;
   while e > 0 do
   begin
      if odd(e) then multiply(res, sq, res);
      multiply(sq, sq, sq);
      e := e div 2
   end
end;

begin
   for i := 1 to n do
      for j := 1 to n do
      begin
         a[i, j] := i + j - 1;
         b[i, j] := i * j
      end;
   writeln('A ='); print(a);
   writeln('B ='); print(b);
   multiply(a, b, c);
   writeln('A * B ='); print(c);
   b[1, 1] := 1; b[1, 2] := 1; b[1, 3] := 0;
   b[2, 1] := 1; b[2, 2] := 0; b[2, 3] := 0;
   b[3, 1] := 0; b[3, 2] := 0; b[3, 3] := 1;
   power(b, 30, c);
   writeln('Fibonacci matrix to the power 30 ='); print(c)
end.
