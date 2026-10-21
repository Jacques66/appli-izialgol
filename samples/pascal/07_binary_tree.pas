{ Binary search tree: insertion with a var parameter of pointer type,
  in-order traversal, height and node count (recursive functions). }
program tree(output);
type ref = ^node;
     node = record
               key, count: integer;
               left, right: ref
            end;
var root: ref;
    i, k: integer;
    seed: integer;

function random(n: integer): integer;
{ linear congruential generator: 0 <= random(n) < n }
begin
   seed := (seed * 421 + 1663) mod 7875;
   random := seed mod n
end;

procedure search(x: integer; var p: ref);
begin
   if p = nil then
   begin
      new(p);
      with p^ do begin key := x; count := 1; left := nil; right := nil end
   end
   else if x < p^.key then search(x, p^.left)
   else if x > p^.key then search(x, p^.right)
   else p^.count := p^.count + 1
end;

procedure printtree(w: ref; depth: integer);
var i: integer;
begin
   if w <> nil then
   begin
      printtree(w^.right, depth + 1);
      for i := 1 to depth do write('    ');
      writeln(w^.key:1, '(', w^.count:1, ')');
      printtree(w^.left, depth + 1)
   end
end;

procedure inorder(w: ref);
begin
   if w <> nil then
   begin
      inorder(w^.left);
      write(w^.key:3);
      inorder(w^.right)
   end
end;

function height(w: ref): integer;
var hl, hr: integer;
begin
   if w = nil then height := 0
   else
   begin
      hl := height(w^.left); hr := height(w^.right);
      if hl > hr then height := hl + 1 else height := hr + 1
   end
end;

function nodes(w: ref): integer;
begin
   if w = nil then nodes := 0
   else nodes := nodes(w^.left) + 1 + nodes(w^.right)
end;

begin
   root := nil; seed := 1;
   for i := 1 to 30 do
   begin
      k := random(40);
      search(k, root)
   end;
   write('in order:'); inorder(root); writeln;
   writeln('nodes: ', nodes(root):1, ', height: ', height(root):1);
   writeln('tree (rotated, root at the left):');
   printtree(root, 0)
end.
