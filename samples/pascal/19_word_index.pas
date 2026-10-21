{ Cross reference of the words of the input text: words (up to 10 letters,
  stored in alfa) are kept in a binary tree; each node holds a linked list
  of the line numbers where the word occurs. Uses pack to build the key. }
program crossref(input, output);
const maxlen = 10;
type wordref = ^word;
     itemref = ^item;
     word = record
               key: alfa;
               first, last: itemref;
               left, right: wordref
            end;
     item = record
               lno: integer;
               next: itemref
            end;
var root: wordref;
    line: integer;
    buf: array[1..maxlen] of char;
    k, i: integer;
    id: alfa;
    letters: set of char;

procedure search(var w: wordref);
var x: itemref;
begin
   if w = nil then
   begin
      new(w); new(x);
      with w^ do begin key := id; left := nil; right := nil; first := x; last := x end;
      x^.lno := line; x^.next := nil
   end
   else if id < w^.key then search(w^.left)
   else if id > w^.key then search(w^.right)
   else if w^.last^.lno <> line then
   begin
      new(x); x^.lno := line; x^.next := nil;
      w^.last^.next := x; w^.last := x
   end
end;

procedure printtree(w: wordref);
var x: itemref;
begin
   if w <> nil then
   begin
      printtree(w^.left);
      write(w^.key, ' ');
      x := w^.first;
      while x <> nil do begin write(x^.lno:3); x := x^.next end;
      writeln;
      printtree(w^.right)
   end
end;

begin
   root := nil; line := 0;
   letters := ['a'..'z', 'A'..'Z'];
   while not eof do
   begin
      line := line + 1;
      while not eoln do
         if input^ in letters then
         begin
            k := 0;
            for i := 1 to maxlen do buf[i] := ' ';
            repeat
               if k < maxlen then begin k := k + 1; buf[k] := input^ end;
               get(input)
            until eoln or not (input^ in letters);
            pack(buf, 1, id);
            search(root)
         end
         else get(input);
      readln
   end;
   printtree(root)
end.
