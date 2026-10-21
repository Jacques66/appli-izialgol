{ Quicksort (C. A. R. Hoare), as in N. Wirth, Algorithms + Data Structures =
  Programs: recursive partitioning of an array passed as a var parameter. }
program quicksort(output);
const n = 20;
type index = 1..n;
     vector = array[index] of integer;
var a: vector;
    i: index;
    seed: integer;

procedure sort(var a: vector; l, r: index);
var i, j: index;
    x, w: integer;
begin
   i := l; j := r;
   x := a[(l + r) div 2];
   repeat
      while a[i] < x do i := i + 1;
      while x < a[j] do j := j - 1;
      if i <= j then
      begin
         w := a[i]; a[i] := a[j]; a[j] := w;
         if i < n then i := i + 1;
         if j > 1 then j := j - 1
      end
   until i > j;
   if l < j then sort(a, l, j);
   if i < r then sort(a, i, r)
end;

procedure print(var a: vector);
var i: index;
begin
   for i := 1 to n do write(a[i]:4);
   writeln
end;

begin
   seed := 17;
   for i := 1 to n do
   begin
      seed := (seed * 1103 + 7) mod 1000;
      a[i] := seed
   end;
   write('before:'); print(a);
   sort(a, 1, n);
   write('after: '); print(a)
end.
