program withconst(input, output);
{ Revised Report 9.2.4: no assignments may be made by the qualified statement
  to any constituents of the record variable list }
type rec = record a, b: integer; next: ^rec end;
var r, s: rec; arr: array[1..3] of rec; i, j: integer; p: ^rec;
begin
   i := 1; new(p);
   with arr[i] do i := 2;
   with arr[i], p^ do begin a := 1; p := nil end;
   with p^.next^ do new(p);
   with arr[i] do for i := 1 to 2 do;
   with arr[i] do read(i);
   with r do r := s;
   with arr[i] do begin j := i; a := j; arr[j].b := 0 end;   { allowed }
   with r do with arr[i] do i := 3
end.
